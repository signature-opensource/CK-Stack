using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CKli.ReleaseDatabase.Plugin;

sealed class ReleaseDB
{
    readonly Dictionary<(Repo Repo, SVersion Version), BuildContentInfo> _data;
    readonly bool _isLocal;
    readonly NormalizedPath _filePath;
    bool _isLoaded;

    internal ReleaseDB( bool isLocal, NormalizedPath filePath )
    {
        _data = new Dictionary<(Repo Repo, SVersion Version), BuildContentInfo>();
        _isLocal = isLocal;
        _filePath = filePath;
    }

    public string Name => _isLocal ? "Local" : "Published";

    public BuildContentInfo? Find( Repo repo, SVersion version ) => _data.GetValueOrDefault( (repo, version) );

    public bool OnLocalBuild( IActivityMonitor monitor, Repo repo, SVersion version, bool rebuild, BuildContentInfo info )
    {
        if( _data.TryGetValue( (repo, version), out var exists ) )
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
                if( _isLocal )
                {
                    monitor.Warn( $"""
                        Updating release database '{Name}' for '{repo.DisplayPath}/{version}':
                        {exists}
                        Replaced by:
                        {info}
                        """ );
                    _data[(repo, version)] = info;
                }
                else
                {
                    monitor.Error( $"""
                        Release database '{Name}' for '{repo.DisplayPath}/{version}' already contains:
                        {exists}
                        The local rebuild generated the following different content:
                        {info}
                        This cannot happen: a released content cannot be different than the initial one.
                        """ );
                }
            }
            else
            {
                monitor.Debug( $"No change in release database '{Name}' for '{repo.DisplayPath}/{version}'." );
            }
        }
        else
        {
            _data.Add( (repo, version), info );
        }
        return true;
    }

    bool EnsureLoad( IActivityMonitor monitor )
    {
        if( _isLoaded ) return true;
        _isLoaded = true;
        if( !File.Exists( _filePath ) )
        {
            monitor.Warn( $"File '{_filePath}' is missing. It will be created." );
            return true;
        }
        return true;
    }

}
