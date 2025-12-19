using CK.Core;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.ArtifactHandler.Plugin;

public sealed class ArtifactHandlerPlugin : PrimaryRepoPlugin<RepoArtifactInfo>
{
    public const string DeployFolderName = "Deployment";
    public const string DeployAssetsName = "Assets";


    readonly NormalizedPath _localFeedPath;
    readonly NormalizedPath _localFeedNuGetPath;
    readonly NormalizedPath _localFeedAssetsPath;

    public ArtifactHandlerPlugin( PrimaryPluginContext context )
        : base( context )
    {
        _localFeedPath = World.StackRepository.StackWorkingFolder.Combine( "$Local/Feed" );
        _localFeedNuGetPath = _localFeedPath.AppendPart( "NuGet" );
        _localFeedAssetsPath = _localFeedPath.AppendPart( DeployAssetsName );
        Directory.CreateDirectory( _localFeedNuGetPath );
        Directory.CreateDirectory( _localFeedAssetsPath );
    }

    /// <summary>
    /// Gets the root "<see cref="StackRepository.StackWorkingFolder"/>/$Local/Feed" folder.
    /// </summary>
    public NormalizedPath LocalFeedPath => _localFeedPath;

    /// <summary>
    /// Gets the "<see cref="LocalFeedPath"/>/NuGet" folder.
    /// </summary>
    public NormalizedPath LocalFeedNuGetPath => _localFeedNuGetPath;

    /// <summary>
    /// Gets the "<see cref="LocalFeedPath"/>/<see cref="DeployAssetsName"/>" folder.
    /// </summary>
    public NormalizedPath LocalFeedAssetsPath => _localFeedAssetsPath;

    /// <summary>
    /// Gets the local folder for assets.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="version">The version.</param>
    /// <returns>The assets local folder (may not exist).</returns>
    public NormalizedPath GetAssetsFolder( Repo repo, SVersion version )
    {
        return _localFeedAssetsPath.AppendPart( repo.DisplayPath.LastPart ).AppendPart( version.ToString() );
    }


    protected override RepoArtifactInfo Create( IActivityMonitor monitor, Repo repo )
    {
        return new RepoArtifactInfo( this, repo );
    }

    public bool HasAllArtifacts( IActivityMonitor monitor, Repo repo, SVersion version, BuildContentInfo buildContentInfo )
    {
        List<string>? missingPackages = null;
        List<string>? missingAssetFileNames = null;
        if( buildContentInfo.Produced.Length > 0 )
        {
            foreach( var p in buildContentInfo.Produced )
            {
                if( !File.Exists( Path.Combine( _localFeedNuGetPath, $"{p}.{version}.nupkg" ) ) )
                {
                    missingPackages ??= new List<string>();
                    missingPackages.Add( p );
                }
            }
        }
        if( buildContentInfo.AssetFileNames.Length > 0 )
        {
            var assetsFolder = GetAssetsFolder( repo, version );
            if( !Directory.Exists( assetsFolder ) )
            {
                missingAssetFileNames = [.. buildContentInfo.AssetFileNames];
            }
            else
            {
                foreach( var f in buildContentInfo.AssetFileNames )
                {
                    if( !File.Exists( assetsFolder.AppendPart( f ) ) )
                    {
                        missingAssetFileNames ??= new List<string>();
                        missingAssetFileNames.Add( f );
                    }

                }
            }
        }
        if( missingPackages == null && missingAssetFileNames == null )
        {
            return true;
        }
        if( missingPackages != null )
        {
            monitor.Info( $"""
            Missing {missingPackages.Count} local packages produced by '{repo.DisplayPath}' for version '{version}':
            {missingPackages.Concatenate()}
            """ );
        }
        if( missingAssetFileNames != null )
        {
            monitor.Info( $"""
            Missing {missingAssetFileNames.Count} asset files produced by '{repo.DisplayPath}' for version '{version}':
            {missingAssetFileNames.Concatenate()}
            """ );
        }
        return false;
    }

    /// <summary>
    /// This should only be called by the VersionTag plugin.
    /// <para>
    /// This is idempotent.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The source repository.</param>
    /// <param name="version">The release to destroy.</param>
    /// <returns>True on success, false if deleting some artifacts failed.</returns>
    public bool DestroyLocalRelease( IActivityMonitor monitor, Repo repo, SVersion version, BuildContentInfo buildContentInfo )
    {
        bool success = true;
        if( buildContentInfo.Produced.Length > 0 )
        {
            foreach( var p in buildContentInfo.Produced )
            {
                NuGetHelper.ClearGlobalCache( monitor, p, version.ToString() );
                success &= FileHelper.DeleteFile( monitor, Path.Combine( _localFeedNuGetPath, $"{p}.{version}.nupkg" ) );
            }
        }
        var assetsFolder = GetAssetsFolder( repo, version );
        success &= FileHelper.DeleteFolder( monitor, assetsFolder );
        return success;
    }
}
