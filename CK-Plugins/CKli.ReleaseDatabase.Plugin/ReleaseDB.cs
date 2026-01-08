using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace CKli.ReleaseDatabase.Plugin;

sealed class ReleaseDB
{
    readonly Dictionary<RepoKey, BuildContentInfo> _data;
    readonly ReleaseDB? _published;
    readonly NormalizedPath _filePath;
    Dictionary<NuGetPackageInstance, RepoKey>? _producedIndex;
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
                                NuGetPackageInstance p,
                                [NotNullWhen(true)] out RepoKey? repo,
                                [NotNullWhen( true )] out BuildContentInfo? content,
                                out bool isLocal )
    {
        Throw.DebugAssert( IsLocal );
        EnsureLoad( monitor );
        if( EnsureProducedIndex().TryGetValue( p, out repo ) )
        {
            content = _data[repo];
            isLocal = true;
            return true;
        }
        isLocal = false;
        _published.EnsureLoad( monitor );
        if( _published.EnsureProducedIndex().TryGetValue( p, out repo ) )
        {
            content = _published._data[repo];
            return true;
        }
        content = null;
        return false;
    }

    Dictionary<NuGetPackageInstance, RepoKey> EnsureProducedIndex()
    {
        Throw.DebugAssert( _isLoaded );
        if( _producedIndex == null )
        {
            _producedIndex = new Dictionary<NuGetPackageInstance, RepoKey>();
            foreach( var e in _data )
            {
                foreach( var id in e.Value.Produced )
                {
                    _producedIndex.Add( new NuGetPackageInstance( id, e.Key.Version ), e.Key );
                }
            }
        }
        return _producedIndex;
    }

    internal void CollectConsumers( IActivityMonitor monitor,
                                    in NuGetPackageInstance p,
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
                _producedIndex.Add( new NuGetPackageInstance( id, key.Version ), key );
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
                _producedIndex.Remove( new NuGetPackageInstance( id, key.Version ) );
            }
            foreach( var id in info.Produced )
            {
                _producedIndex.Add( new NuGetPackageInstance( id, key.Version ), key );
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
                _producedIndex.Remove( new NuGetPackageInstance( id, key.Version ) );
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
                    _producedIndex.Remove( new NuGetPackageInstance( id, version ) );
                }
            }
            OnChanged( monitor );
        }
        return exists;
    }

}
