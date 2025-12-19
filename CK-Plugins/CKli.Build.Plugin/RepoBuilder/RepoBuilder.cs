using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using System;
using System.Collections.Immutable;
using System.IO;

namespace CKli.Build.Plugin;

/// <summary>
/// Concrete base class that can be specialized.
/// The <see cref="RepositoryBuilderPlugin.Create(IActivityMonitor, Repo)"/> acts as an abstract factory:
/// the actual RepoBuilder can differ for each Repo.
/// </summary>
public class RepoBuilder : RepoInfo
{
    readonly LocalStringCache _shaTestRunCache;
    readonly RepoArtifactInfo _repoArtifact;

    public RepoBuilder( Repo repo, LocalStringCache shaTestRunCache, RepoArtifactInfo repoArtifact )
        : base( repo )
    {
        _shaTestRunCache = shaTestRunCache;
        _repoArtifact = repoArtifact;
    }

    /// <summary>
    /// Build, test, package and generate the optional Deployment assets of the repository.
    /// On success, the <see cref="BuildResult"/> contains the consumed and produced NuGet packages
    /// and the files to deploy.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="buildInfo">The build info.</param>
    /// <param name="runTest"></param>
    /// <returns></returns>
    public BuildResult? Build( IActivityMonitor monitor,
                               CommitBuildInfo buildInfo,
                               bool? runTest )
    {
        Throw.CheckState( "Repository must not be dirty when calling build.",
                          !Repo.GitRepository.GetSimpleStatusInfo().IsDirty );
        Throw.CheckArgument( buildInfo.Repo == Repo );

        var outputPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath() + "CKliBuild", null, DateTime.UtcNow );
        try
        {
            if( DoBuild( monitor,
                         buildInfo.Version,
                         buildInfo.InformationalVersion,
                         buildInfo.FileVersion,
                         buildInfo.ReleaseConfiguration,
                         runTest,
                         outputPath ) )
            {
                var consumedPackages = BuildResult.GetConsumedPackages( monitor, buildInfo );
                if( consumedPackages != null )
                {
                    var deploymentFolder = Path.Combine( Repo.WorkingFolder, ArtifactHandlerPlugin.DeployFolderName );
                    if( HandleDeployAssets( monitor, deploymentFolder, buildInfo.Version, out var assetsFolder, out var assetFileNames ) )
                    {
                        if( _repoArtifact.PublishToNuGetLocalFeed( monitor, buildInfo.Version, outputPath, out var publishedPackages ) )
                        {
                            var r = new BuildResult( Repo,
                                                     buildInfo.Version,
                                                     consumedPackages,
                                                     publishedPackages,
                                                     assetsFolder,
                                                     assetFileNames );
                            outputPath = null;
                            return r;
                        }
                    }
                }
            }
        }
        finally
        {
            if( outputPath != null ) FileHelper.DeleteFolder( monitor, outputPath );
        }
        return null;
    }

    bool HandleDeployAssets( IActivityMonitor monitor,
                             string deploymentFolder,
                             SVersion version,
                             out NormalizedPath assetsFolder,
                             out ImmutableArray<string> fileNames )
    {
        assetsFolder = default;
        fileNames = [];
        if( !Directory.Exists( deploymentFolder ) )
        {
            // No Deployment assets.
            monitor.Trace( $"No '{Repo.DisplayPath}/{ArtifactHandlerPlugin.DeployFolderName}' folder." );
            return true;
        }
        using var gLog = monitor.OpenInfo( $"Handling {ArtifactHandlerPlugin.DeployFolderName}/{ArtifactHandlerPlugin.DeployAssetsName}." );
        try
        {
            var generateFileApp = Path.Combine( deploymentFolder, "GenerateAssets.cs" );
            if( !File.Exists( generateFileApp ) )
            {
                // Not an error.
                monitor.Warn( $"Ignoring '{Repo.DisplayPath}/{ArtifactHandlerPlugin.DeployFolderName}' because no 'GenerateAssets.cs' file exists." );
                return true;
            }
            var repoAssetsFolder = Path.Combine( deploymentFolder, "Assets" );
            if( Directory.Exists( repoAssetsFolder ) )
            {
                monitor.Trace( $"Cleaning up '{Repo.DisplayPath}/{ArtifactHandlerPlugin.DeployFolderName}/{ArtifactHandlerPlugin.DeployAssetsName}'." );
                if( !FileHelper.DeleteFolder( monitor, assetsFolder ) )
                {
                    return false;
                }
            }
            Directory.CreateDirectory( repoAssetsFolder );
            var e = ProcessRunner.RunProcess( monitor.ParallelLogger,
                                              "dotnet",
                                              $"run GenerateAssets.cs -- {version}",
                                              deploymentFolder );
            if( e != 0 )
            {
                monitor.Error( $"Running '{Repo.DisplayPath}/{ArtifactHandlerPlugin.DeployFolderName}/GenerateAssets.cs' failed with code '{e}'." );
                return false;
            }
            return _repoArtifact.PublishGeneratedAssets( monitor, version, out assetsFolder, out fileNames );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While handling '{ArtifactHandlerPlugin.DeployFolderName}/{ArtifactHandlerPlugin.DeployAssetsName}' creation.", ex );
            return false;
        }
    }

    /// <summary>
    /// Core Build method.
    /// By default, this calls the <see cref="DotNetBuildTestPack(IActivityMonitor, SVersion, string, bool, bool?, string)"/> helper.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to build.</param>
    /// <param name="informationalVersion">The informational version to set (see <see cref="InformationalVersion"/>).</param>
    /// <param name="fileVersion">The windows file version. See <see cref="CommitBuildInfo.FileVersion"/>.</param>
    /// <param name="release">False to use Debug build configuration.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected virtual bool DoBuild( IActivityMonitor monitor,
                                    SVersion version,
                                    string informationalVersion,
                                    string fileVersion,
                                    bool release,
                                    bool? runTest,
                                    NormalizedPath outputPath )
    {
        return DotNetBuildTestPack( monitor, version, informationalVersion, fileVersion, release, runTest, outputPath );
    }

    /// <summary>
    /// Helper that calls <see cref="DotNetBuild(IActivityMonitor, SVersion, string, bool)"/>,
    /// <see cref="DotNetTest(IActivityMonitor, bool?)"/> and <see cref="DotNetPack(IActivityMonitor, string)"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to build.</param>
    /// <param name="informationalVersion">The informational version to set (see <see cref="CSemVer.InformationalVersion"/>).</param>
    /// <param name="fileVersion">The windows file version. See <see cref="CommitBuildInfo.FileVersion"/>.</param>
    /// <param name="release">False to use Debug build configuration.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected virtual bool DotNetBuildTestPack( IActivityMonitor monitor,
                                                SVersion version,
                                                string informationalVersion,
                                                string fileVersion,
                                                bool release,
                                                bool? runTest,
                                                string outputPath )
    {
        return DotNetBuild( monitor, version, informationalVersion, fileVersion, release )
               && DotNetTest( monitor, runTest )
               && DotNetPack( monitor, version, outputPath );
    }

    /// <summary>
    /// Helper that calls "dotnet build".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to build.</param>
    /// <param name="informationalVersion">The informational version to set (see <see cref="CSemVer.InformationalVersion"/>).</param>
    /// <param name="fileVersion">The windows file version. See <see cref="CommitBuildInfo.FileVersion"/>.</param>
    /// <param name="release">False to use Debug build configuration.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetBuild( IActivityMonitor monitor, SVersion version, string informationalVersion, string fileVersion, bool release )
    {
        return BuildPlugin.RunDotnet( monitor, Repo, $"""
            build -tl:off --nologo -c {(release ? "Release" : "Debug")} /p:Version={version};InformationalVersion="{informationalVersion}";FileVersion="{fileVersion}"
            """ );
    }

    /// <summary>
    /// Helper that calls "dotnet test" and handles the <paramref name="runTest"/> to skip the
    /// tests if they have already run.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetTest( IActivityMonitor monitor, bool? runTest )
    {
        string testKey = Repo.GitRepository.Repository.Head.Tip.Tree.Sha;
        runTest ??= !_shaTestRunCache.Contains( monitor, testKey );
        if( runTest is true )
        {
            if( !BuildPlugin.RunDotnet( monitor, Repo, $"test -tl:off --nologo --no-build" ) )
            {
                return false;
            }
            _shaTestRunCache.Add( monitor, testKey );
        }
        return true;
    }

    /// <summary>
    /// Helper that calls "dotnet pack".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to pack.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetPack( IActivityMonitor monitor, SVersion version, string outputPath )
    {
        return BuildPlugin.RunDotnet( monitor, Repo, $"""pack -tl:off /p:Version={version} --nologo --no-build -o "{outputPath}" """ );
    }


}


