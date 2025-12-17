using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace CKli.ReleaseDatabase.Plugin;

sealed class ReleaseDB
{
    readonly Dictionary<(ulong RepoId, SVersion Version), BuildContentInfo> _data;
    readonly ReleaseDB? _published;
    readonly NormalizedPath _filePath;
    bool _isLoaded;

    internal ReleaseDB( ReleaseDB? published, NormalizedPath filePath )
    {
        _data = new Dictionary<(ulong RepoId, SVersion Version), BuildContentInfo>();
        _published = published;
        _filePath = filePath;
    }

    [MemberNotNullWhen( true, nameof( _published ) )]
    public bool IsLocal => _published != null;

    public string Name => IsLocal ? "Local" : "Published";

    public BuildContentInfo? Find( Repo repo, SVersion version ) => _data.GetValueOrDefault( (repo.CKliRepoId.Value, version) );

    internal World.Issue? OnExistingVersionTags( IActivityMonitor monitor, ScreenType screenType, Repo repo, IEnumerable<(SVersion, BuildContentInfo)> versions )
    {
        Throw.DebugAssert( IsLocal );
        _published.EnsureLoad( monitor );
        EnsureLoad( monitor );

        bool localChanged = false;
        List<(SVersion V, BuildContentInfo Pub, BuildContentInfo Tag)>? issues = null; 
        ulong repoId = repo.CKliRepoId.Value;
        foreach( var (version,info) in versions )
        {
            var key = (repoId, version);
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
                        _data[key] = info;
                        localChanged = true;
                    }
                }
                else
                {
                    _data.Add( key, info );
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

        var key = (repo.CKliRepoId.Value, version);
        if( _published._data.TryGetValue( key, out var exists ) )
        {
            return OnAlreadyLocalBuild( monitor, repo, version, rebuild, info, key, exists );
        }
        if( _data.TryGetValue( key, out exists ) )
        {
            return OnAlreadyLocalBuild( monitor, repo, version, rebuild, info, key, exists );
        }
        _data.Add( key, info );
        return OnChanged( monitor );
    }

    bool OnAlreadyLocalBuild( IActivityMonitor monitor,
                              Repo repo,
                              SVersion version,
                              bool rebuild,
                              BuildContentInfo info,
                              (ulong Value, SVersion version) key,
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
                _data[key] = info;
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

        var key = (repo.CKliRepoId.Value, version);
        if( _published._data.ContainsKey( key ) )
        {
            return true;
        }
        if( _data.TryGetValue( key, out var info ) )
        {
            _published._data.Add( key, info );
            if( !_published.OnChanged( monitor ) )
            {
                return false;
            }
            _data.Remove( key );
            return OnChanged( monitor );
        }
        monitor.Error( $"Unable to find a release for '{repo.DisplayPath}/{version}' in Local or Published release databases." );
        return false;
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
            using var f = new FileStream( _filePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.SequentialScan );
            using var r = new CKBinaryReader( f, Encoding.UTF8, leaveOpen: true );
            Throw.CheckData( "Version", r.ReadByte() == 0 );
            int count = r.ReadInt32();
            while( --count >= 0 )
            {
                _data.Add( (r.ReadUInt64(), SVersion.Parse( r.ReadString() )), new BuildContentInfo( r ) );
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
            using var f = new FileStream( _filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan );
            using var w = new CKBinaryWriter( f, Encoding.UTF8, leaveOpen: true );
            w.Write( (byte)0 ); // Version.
            w.Write( _data.Count );
            foreach( var (k, v) in _data )
            {
                w.Write( k.RepoId );
                w.Write( k.Version.ToString() );
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
}
