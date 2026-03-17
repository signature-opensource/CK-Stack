using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Encapsulates a temporary folder (that should be deleted when done) that contains
/// the packages to push.
/// </summary>
sealed class NuGetPushFolder
{
    static bool _symLinkPrivilegeError;

    readonly string _pushFolder;
    readonly int _count;

    NuGetPushFolder( int count, string publishTempFolder )
    {
        _count = count;
        _pushFolder = publishTempFolder;
    }

    /// <summary>
    /// Gets the number of packages to push.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets the path of the temporary folder that contains the .nupkg files to be pushed.
    /// </summary>
    public string PushFolder => _pushFolder;

    public static NuGetPushFolder? Create( IActivityMonitor monitor,
                                           NormalizedPath localNuGetPath,
                                           IEnumerable<BuildResult> results )
    {
        string pushFolder = Path.Combine( Path.GetTempPath(), "CKliPub", Path.GetTempFileName() );
        Directory.CreateDirectory( pushFolder );
        int count = 0;
        foreach( var r in results )
        {
            foreach( var p in r.Produced )
            {
                var fileName = p.ToNupkgFileName();
                if( !SymbolicCopyFile( monitor,
                                       Path.Combine( localNuGetPath, fileName ),
                                       Path.Combine( pushFolder, fileName ),
                                       allowOverwrite: false ) )
                {
                    FileHelper.DeleteFolder( monitor, pushFolder );
                    return null;
                }
                ++count;
            }
        }
        return new NuGetPushFolder( count, pushFolder );

        static bool SymbolicCopyFile( IActivityMonitor logger, string source, string destination, bool allowOverwrite )
        {
            if( !_symLinkPrivilegeError )
            {
                try
                {
                    if( allowOverwrite && !FileHelper.DeleteFile( logger, destination ) )
                    {
                        return false;
                    }
                    File.CreateSymbolicLink( destination, source );
                    return true;
                }
                catch( Exception ex )
                {
                    // A required privilege is not held by the client (0x80070522).
                    if( ex.HResult == -2147023582 )
                    {
                        _symLinkPrivilegeError = true;
                        logger.Warn( $"Not enough privilege to create symbolic link. Disabling Symbolic file and folder handling." );
                    }
                    else
                    {
                        logger.Error( $"While updating symbolic link '{destination}' to '{source}'.", ex );
                        return false;
                    }
                }
            }
            return CopyFile( logger, source, destination, allowOverwrite );
        }

        static bool CopyFile( IActivityLineEmitter logger, string source, string destination, bool allowOverwrite )
        {
            int tryCount = 0;
            for(; ; )
            {
                try
                {
                    if( !allowOverwrite && File.Exists( destination ) )
                    {
                        logger.Error( $"Destination file '{destination}' already exists. Cannot copy from '{source}'." );
                        return false;
                    }
                    File.Copy( source, destination, allowOverwrite );
                    return true;
                }
                catch( Exception ex )
                {
                    if( ++tryCount > 5 )
                    {
                        logger.Error( $"While copying file '{source}' to '{destination}'.", ex );
                        return false;
                    }
                }
            }
        }
    }
}
