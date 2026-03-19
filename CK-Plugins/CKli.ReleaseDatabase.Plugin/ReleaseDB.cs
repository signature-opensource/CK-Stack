using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Cache;
using System.Text;

namespace CKli.ReleaseDatabase.Plugin;

sealed class ReleaseDB
{
    readonly Dictionary<RepoKey, BuildContentInfo> _data;
    readonly ReleaseDB? _published;
    readonly NormalizedPath _filePath;
    Dictionary<PackageInstance, RepoKey>? _producedIndex;
    bool _isLoaded;

    internal ReleaseDB( ReleaseDB? published, NormalizedPath filePath )
    {
        _data = new Dictionary<RepoKey, BuildContentInfo>();
        _published = published;
        _filePath = filePath;
    }

    [MemberNotNullWhen( true, nameof( _published ) )]
    public bool IsLocal => _published != null;

    public string Name => IsLocal ? "Local" : "Published";

    internal BuildContentInfo? Find( IActivityMonitor monitor, Repo repo, SVersion version, out bool local )
    {
        return Find( monitor, new RepoKey( repo.CKliRepoId, version ), out local );
    }

    internal BuildContentInfo? Find( IActivityMonitor monitor, RepoKey key, out bool local )
    {
        Throw.DebugAssert( IsLocal );
        EnsureLoad( monitor );
        if( _data.TryGetValue( key, out var c ) )
        {
            local = true;
            return c;
        }
        _published.EnsureLoad( monitor );
        local = false;
        return _published._data.GetValueOrDefault( key );
    }

    internal bool FindProducer( IActivityMonitor monitor,
                                World world,
                                PackageInstance p,
                                [NotNullWhen(true)] out RepoKey? repo,
                                [NotNullWhen( true )] out BuildContentInfo? content,
                                out bool isLocal )
    {
        Throw.DebugAssert( IsLocal );
        EnsureLoad( monitor );
        if( EnsureProducedIndex( monitor, world ).TryGetValue( p, out repo ) )
        {
            content = _data[repo];
            isLocal = true;
            return true;
        }
        isLocal = false;
        _published.EnsureLoad( monitor );
        if( _published.EnsureProducedIndex( monitor, world ).TryGetValue( p, out repo ) )
        {
            content = _published._data[repo];
            return true;
        }
        content = null;
        return false;
    }

    Dictionary<PackageInstance, RepoKey> EnsureProducedIndex( IActivityMonitor monitor, World world )
    {
        Throw.DebugAssert( _isLoaded );
        if( _producedIndex == null )
        {
            _producedIndex = CreateIndex( monitor, _data, null );
            if( _producedIndex == null )
            {
                monitor.Warn( $"Potential obsolete ckli-repo identifiers found in {Name} release database. Trying to automatically fix this." );
                var repos = world.GetAllDefinedRepo( monitor );
                if( repos == null )
                {
                    throw new CKException( $"Unable to load all repositories while trying to remove obsolete ckli-repo identifiers from {Name} release database. Giving up." );
                }
                RemoveDeadRepositories( monitor, world, _data );
                _producedIndex = CreateIndex( monitor, _data, world );
                Throw.DebugAssert( _producedIndex != null );
            }
        }
        return _producedIndex;

        static Dictionary<PackageInstance, RepoKey>? CreateIndex( IActivityMonitor monitor,
                                                                  Dictionary<RepoKey, BuildContentInfo> data,
                                                                  World? world )
        {
            var index = new Dictionary<PackageInstance, RepoKey>();
            foreach( var e in data )
            {
                foreach( var id in e.Value.Produced )
                {
                    var p = new PackageInstance( id, e.Key.Version );
                    if( !index.TryAdd( p, e.Key ) )
                    {
                        // The same package instance is recorded to have been produced
                        // by two different RepoKey. if the conflicting RepoKeys have different
                        // RepoId, then one of them is obsolete but if the versions differ then
                        // it is a more serious incoherency.
                        var exists = index[p];
                        if( exists.RepoId == e.Key.RepoId )
                        {
                            throw new CKException( $"""
                                Package '{p}' is recorded to be produced by version '{exists.Version}' and '{e.Key.Version}' of repository with ckli-repo '{exists.RepoId}'.
                                This is totally incoherent, the release database must be rebuilt.
                                Please use the command 'ckli maintenance release-database rebuild'.
                                """ );
                        }
                        // Two ckli-repo identifier. We have a way to automatically fix this:
                        // considering the current world's repositories, only one should exist, we return null and RemoveDeadRepositories
                        // will be called (or this is over).
                        if( world == null ) return null;
                        // This is over...
                        var existR = world.FindByCKliRepoId( monitor, exists.RepoId );
                        Throw.DebugAssert( existR != null );
                        var keyR = world.FindByCKliRepoId( monitor, e.Key.RepoId );
                        Throw.DebugAssert( keyR != null );
                        throw new CKException( $"""
                                Package '{p}' is recorded to be produced by both '{existR.DisplayPath}@{exists.Version}' (ckli-repo: '{exists.RepoId}') and '{keyR.DisplayPath}@{e.Key.Version}' '(ckli-repo: '{e.Key.RepoId}').
                                This is totally incoherent, the release database must be rebuilt. If the problem persists, one of the tags in the repositories is necessarily invalid and should be deleted.
                                Please use the command 'ckli maintenance release-database rebuild'.
                                """ );
                    }
                }
            }
            return index;
        }

        static void RemoveDeadRepositories( IActivityMonitor monitor, World world, Dictionary<RepoKey, BuildContentInfo> data )
        {
            List<RepoKey>? toRemove = null;
            foreach( var key in data.Keys )
            {
                if( world.FindByCKliRepoId( monitor, key.RepoId, alreadyLoadedOnly: true ) == null )
                {
                    toRemove ??= new List<RepoKey>();
                    toRemove.Add( key );
                }
            }
            if( toRemove != null )
            {
                var b = new StringBuilder();
                foreach( var key in toRemove )
                {
                    b.Append( b.Length == 0 ? "Removed ckli-repo identifiers that don't exist anymore: '" : "', '" );
                    b.Append( key.RepoId.ToString() );
                    data.Remove( key );
                }
                monitor.Info( b.Append( "'." ).ToString() );
            }
        }

    }

    internal void CollectConsumers( IActivityMonitor monitor,
                                    in PackageInstance p,
                                    Dictionary<RepoKey, (BuildContentInfo Content, bool IsLocal)> collector )
    {
        EnsureLoad( monitor );
        foreach( var d in _data )
        {
            if( d.Value.Consumed.Contains( p ) )
            {
                Throw.DebugAssert( """
                    The local DB must first be challenged:
                    if the consumer has already been found, then it has been found in the local db
                    or in the published db and we are in the published db. 
                    """, !collector.TryGetValue( d.Key, out var exist ) || exist.IsLocal || exist.IsLocal == IsLocal);
                collector.TryAdd( d.Key, (d.Value, IsLocal) );
            }
        }
    }

    internal World.Issue? OnExistingVersionTags( IActivityMonitor monitor, ScreenType screenType, Repo repo, IEnumerable<(SVersion, BuildContentInfo)> versions )
    {
        Throw.DebugAssert( IsLocal );
        _published.EnsureLoad( monitor );
        EnsureLoad( monitor );

        bool localChanged = false;
        List<(SVersion V, BuildContentInfo Pub, BuildContentInfo Tag)>? issues = null; 
        foreach( var (version,info) in versions )
        {
            var key = new RepoKey( repo.CKliRepoId, version);
            if( _published._data.TryGetValue( key, out var exists ) )
            {
                if( info != exists )
                {
                    issues ??= new List<(SVersion V, BuildContentInfo Pub, BuildContentInfo Tag)>();
                    issues.Add( (version, exists, info) );
                }
            }
            else
            {
                if( _data.TryGetValue( key, out exists ) )
                {
                    if( info != exists )
                    {
                        monitor.Warn( $"""
                            Updating release database '{Name}' for '{repo.DisplayPath}/{version}':
                            {exists}
                            Replaced by:
                            {info}
                            """ );
                        DataUpdate( key, exists, info );
                        localChanged = true;
                    }
                }
                else
                {
                    DataAdd( key, info );
                    localChanged = true;
                }
            }
        }
        if( localChanged )
        {
            Save( monitor );
        }
        if( issues != null )
        {
            var text = issues.Select( i => $"""
            - Version tag '{i.V.ParsedText}' declares to contain:
            {i.Tag}
            But Published release database contains:
            {i.Pub}
            """ ).Concatenate( Environment.NewLine );
            return World.Issue.CreateManual( "Difference in Published release content.",
                                             screenType.Text( $"""
                                                 {text}

                                                 The tag content should reflect the published release.
                                                 """),
                                             repo );
        }
        return null;
    }

    internal bool OnLocalBuild( IActivityMonitor monitor, Repo repo, SVersion version, bool rebuild, BuildContentInfo info )
    {
        Throw.DebugAssert( IsLocal );
        _published.EnsureLoad( monitor );
        EnsureLoad( monitor );

        var key = new RepoKey(repo.CKliRepoId, version);
        if( _published._data.TryGetValue( key, out var exists ) )
        {
            return OnAlreadyLocalBuild( monitor, repo, version, rebuild, info, key, exists );
        }
        if( _data.TryGetValue( key, out exists ) )
        {
            return OnAlreadyLocalBuild( monitor, repo, version, rebuild, info, key, exists );
        }
        DataAdd( key, info );
        return OnChanged( monitor );
    }

    bool OnAlreadyLocalBuild( IActivityMonitor monitor,
                              Repo repo,
                              SVersion version,
                              bool rebuild,
                              BuildContentInfo info,
                              RepoKey key,
                              BuildContentInfo exists )
    {
        if( !rebuild )
        {
            // Whether this is the local or published database is irrelevant here: if it's not a rebuild
            // then the released content MUST NOT already exist.
            monitor.Error( $"""
                    Release database '{Name}' already contains a content for '{repo.DisplayPath}/{version}'.
                    This release can only be rebuilt.
                    """ );
            return false;
        }
        if( !exists.Equals( info ) )
        {
            if( IsLocal )
            {
                monitor.Warn( $"""
                        Updating release database '{Name}' for '{repo.DisplayPath}/{version}':
                        {exists}
                        Replaced by:
                        {info}
                        """ );
                DataUpdate( key, exists, info );
                return OnChanged( monitor );
            }
            monitor.Error( $"""
                    Release database '{Name}' for '{repo.DisplayPath}/{version}' already contains:
                    {exists}
                    The local rebuild generated the following different content:
                    {info}
                    This cannot happen: a released content cannot be different than the initial one.
                    """ );
            return false;
        }
        monitor.Debug( $"No change in release database '{Name}' for '{repo.DisplayPath}/{version}'." );
        return true;
    }

    internal bool PublishRelease( IActivityMonitor monitor, Repo repo, SVersion version )
    {
        Throw.DebugAssert( IsLocal );
        _published.EnsureLoad( monitor );
        EnsureLoad( monitor );

        var key = new RepoKey(repo.CKliRepoId, version);
        if( _published._data.ContainsKey( key ) )
        {
            return true;
        }
        if( _data.TryGetValue( key, out var info ) )
        {
            _published.DataAdd( key, info );
            if( !_published.OnChanged( monitor ) )
            {
                return false;
            }
            DataRemove( key, info );
            return OnChanged( monitor );
        }
        monitor.Error( $"Unable to find a release for '{repo.DisplayPath}/{version}' in Local or Published release databases." );
        return false;
    }

    void DataAdd( RepoKey key, BuildContentInfo info )
    {
        _data.Add( key, info );
        if( _producedIndex != null )
        {
            foreach( var id in info.Produced )
            {
                _producedIndex.Add( new PackageInstance( id, key.Version ), key );
            }
        }
    }

    void DataUpdate( RepoKey key, BuildContentInfo exists, BuildContentInfo info )
    {
        Throw.DebugAssert( _data[key] == exists && exists != info );
        _data[key] = info;
        if( _producedIndex != null && !exists.Produced.SequenceEqual( info.Produced ) )
        {
            foreach( var id in exists.Produced )
            {
                _producedIndex.Remove( new PackageInstance( id, key.Version ) );
            }
            foreach( var id in info.Produced )
            {
                _producedIndex.Add( new PackageInstance( id, key.Version ), key );
            }
        }
    }

    void DataRemove( RepoKey key, BuildContentInfo exists )
    {
        _data.Remove( key );
        if( _producedIndex != null )
        {
            foreach( var id in exists.Produced )
            {
                _producedIndex.Remove( new PackageInstance( id, key.Version ) );
            }
        }
    }

    bool OnChanged( IActivityMonitor monitor )
    {
        // Currently, we save.
        // This may change to support "transactions" if needed.
        return Save( monitor );
    }

    bool EnsureLoad( IActivityMonitor monitor )
    {
        if( _isLoaded ) return true;
        _isLoaded = true;
        if( !File.Exists( _filePath ) )
        {
            monitor.Warn( $"Release database cache '{Name}' is missing. It will be created." );
            return true;
        }
        try
        {
            using var r = new CKBinaryReader( new FileStream( _filePath,
                                                              FileMode.Open,
                                                              FileAccess.Read,
                                                              FileShare.None,
                                                              4096,
                                                              FileOptions.SequentialScan ) );
            Throw.CheckData( "Version", r.ReadByte() == 0 );
            int count = r.ReadInt32();
            while( --count >= 0 )
            {
                var id = new RandomId( r.ReadUInt64() );
                var v = SVersion.Parse( r.ReadSharedString() );
                var c = new BuildContentInfo( r );
                _data.Add( new RepoKey( id, v ), c );
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading '{_filePath}'.", ex );
            return false;
        }
    }

    bool Save( IActivityMonitor monitor )
    {
        try
        {
            using var w = new CKBinaryWriter( new FileStream( _filePath,
                                                              FileMode.Create,
                                                              FileAccess.Write,
                                                              FileShare.None,
                                                              4096,
                                                              FileOptions.SequentialScan ) );
            w.Write( (byte)0 ); // Version.
            w.Write( _data.Count );
            foreach( var (k, v) in _data )
            {
                w.Write( k.RepoId.Value );
                w.WriteSharedString( k.Version.ToString() );
                v.Write( w );
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"While saving '{_filePath}'.", ex );
            return false;
        }
        return true;
    }

    internal BuildContentInfo? DestroyLocalRelease( IActivityMonitor monitor, Repo repo, SVersion version )
    {
        Throw.DebugAssert( IsLocal );
        EnsureLoad( monitor );
        if( _data.Remove( new RepoKey( repo.CKliRepoId, version ), out var exists ) )
        {
            monitor.Info( $"Removed version '{repo.DisplayPath}/{version}' from {Name} release database." );
            if( _producedIndex != null )
            {
                foreach( var id in exists.Produced )
                {
                    _producedIndex.Remove( new PackageInstance( id, version ) );
                }
            }
            OnChanged( monitor );
        }
        return exists;
    }

    internal void DestroyAllLocalFixRelease( IActivityMonitor monitor )
    {
        Throw.DebugAssert( IsLocal );
        EnsureLoad( monitor );
        var toRemove = _data.Keys.Where( k => k.Version.IsLocalFix() ).ToList();
        if( toRemove.Count > 0 )
        {
            monitor.Info( $"Removing {toRemove.Count} local fix versions from local release database." );
            foreach( var fix in toRemove )
            {
                if( _data.Remove( fix, out var exists ) && _producedIndex != null )
                {
                    foreach( var id in exists.Produced )
                    {
                        _producedIndex.Remove( new PackageInstance( id, fix.Version ) );
                    }
                }
            }
            OnChanged( monitor );
        }
    }


    internal void Destroy( IActivityMonitor monitor, bool createBackup )
    {
        Throw.CheckState( !_isLoaded );
        if( File.Exists( _filePath ) )
        {
            if( createBackup )
            {
                var backup = _filePath.Path + ".bak";
                FileHelper.DeleteFile( monitor, backup );
                File.Move( _filePath, backup );
            }
            else
            {
                FileHelper.DeleteFile( monitor, _filePath );
            }
        }
    }
}
