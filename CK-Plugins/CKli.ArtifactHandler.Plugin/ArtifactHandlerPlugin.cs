using CK.Core;
using CKli.Core;
using CSemVer;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace CKli.ArtifactHandler.Plugin;

public sealed class ArtifactHandlerPlugin : PrimaryRepoPlugin<RepoArtifactInfo>
{
    public const string DeployFolderName = "Deployment";
    public const string DeployAssetsName = "Assets";


    readonly NormalizedPath _localPath;
    readonly NormalizedPath _localNuGetPath;
    readonly NormalizedPath _localAssetsPath;

    public ArtifactHandlerPlugin( PrimaryPluginContext context )
        : base( context )
    {
        _localPath = World.StackRepository.StackWorkingFolder.AppendPart( "$Local" ).AppendPart( World.Name.FullName );
        _localNuGetPath = _localPath.AppendPart( "NuGet" );
        _localAssetsPath = _localPath.AppendPart( DeployAssetsName );
        Directory.CreateDirectory( _localNuGetPath );
        Directory.CreateDirectory( _localAssetsPath );
    }

    /// <summary>
    /// Gets the root "$Local/&lt;world name&gt;" folder.
    /// </summary>
    public NormalizedPath LocalPath => _localPath;

    /// <summary>
    /// Gets the "$Local/&lt;world name&gt;/NuGet" folder.
    /// </summary>
    public NormalizedPath LocalNuGetPath => _localNuGetPath;

    /// <summary>
    /// Gets the "$Local/&lt;world name&gt;/Assets" folder.
    /// </summary>
    public NormalizedPath LocalAssetsPath => _localAssetsPath;

    /// <summary>
    /// Gets the local folder for assets.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="version">The version.</param>
    /// <returns>The assets local folder (may not exist).</returns>
    public NormalizedPath GetAssetsFolder( Repo repo, SVersion version )
    {
        return _localAssetsPath.AppendPart( repo.DisplayPath.LastPart ).AppendPart( version.ToString() );
    }

    protected override RepoArtifactInfo Create( IActivityMonitor monitor, Repo repo )
    {
        return new RepoArtifactInfo( this, repo );
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The released repository.</param>
    /// <param name="version">The released version.</param>
    /// <param name="buildContentInfo">The release info.</param>
    /// <param name="assetsFolder">
    /// Outputs the "$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;" where artifacts are.
    /// This is <see cref="NormalizedPath.IsEmptyPath"/> if <see cref="BuildContentInfo.AssetFileNames"/> is empty.
    /// </param>
    /// <returns>True if the release's packages and asset files are locally available, false otherwise.</returns>
    public bool HasAllArtifacts( IActivityMonitor monitor,
                                 Repo repo,
                                 SVersion version,
                                 BuildContentInfo buildContentInfo,
                                 out NormalizedPath assetsFolder )
    {
        List<string>? missingPackages = null;
        List<string>? missingAssetFileNames = null;
        if( buildContentInfo.Produced.Length > 0 )
        {
            foreach( var p in buildContentInfo.Produced )
            {
                if( !File.Exists( Path.Combine( _localNuGetPath, $"{p}.{version}.nupkg" ) ) )
                {
                    missingPackages ??= new List<string>();
                    missingPackages.Add( p );
                }
            }
        }
        if( buildContentInfo.AssetFileNames.Length > 0 )
        {
            assetsFolder = GetAssetsFolder( repo, version );
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
        else
        {
            assetsFolder = default;
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
                success &= FileHelper.DeleteFile( monitor, Path.Combine( _localNuGetPath, $"{p}.{version}.nupkg" ) );
            }
        }
        var assetsFolder = GetAssetsFolder( repo, version );
        success &= FileHelper.DeleteFolder( monitor, assetsFolder );
        return success;
    }
}
