using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;

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
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <returns>True on success, false otherwise.</returns>
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
                if( BuildResult.GetConsumedPackages( monitor, buildInfo, out var consumedPackages ) )
                {
                    var deploymentFolder = Repo.WorkingFolder.AppendPart( ArtifactHandlerPlugin.DeployFolderName );
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
                             NormalizedPath deploymentFolder,
                             SVersion version,
                             out NormalizedPath assetsFolder,
                             out ImmutableArray<string> fileNames )
    {
        assetsFolder = default;
        fileNames = [];
        if( !Directory.Exists( deploymentFolder ) )
        {
            // No Deployment assets.
            monitor.Trace( $"No '{deploymentFolder}' folder." );
            return true;
        }
        using var gLog = monitor.OpenInfo( $"Handling {deploymentFolder}." );
        try
        {
            var generateFileApp = deploymentFolder.AppendPart( "GenerateAssets.cs" );
            if( !File.Exists( generateFileApp ) )
            {
                // Not an error.
                monitor.Warn( $"Ignoring '{deploymentFolder}' because no 'GenerateAssets.cs' file exists." );
                return true;
            }
            var repoAssetsFolder = deploymentFolder.AppendPart( ArtifactHandlerPlugin.DeployAssetsName );
            if( Directory.Exists( repoAssetsFolder ) )
            {
                monitor.Trace( $"Cleaning up '{repoAssetsFolder}'." );
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
                monitor.Error( $"Running '{generateFileApp}' failed with code '{e}'." );
                return false;
            }
            return _repoArtifact.PublishGeneratedAssets( monitor, version, out assetsFolder, out fileNames );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While handling '{deploymentFolder}' creation.", ex );
            return false;
        }
    }

    /// <summary>
    /// Core Build method.
    /// By default, this calls the <see cref="DotNetBuildTestPack"/> helper.
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
    /// Helper that calls <see cref="DotNetBuild"/>, <see cref="DotNetTest"/> and <see cref="DotNetPack"/>.
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
    protected bool DotNetBuild( IActivityMonitor monitor,
                                SVersion version,
                                string informationalVersion,
                                string fileVersion,
                                bool release )
    {
        var localFeed = _repoArtifact.LocalFeedNuGetPath;
        //
        // Calling "dotnet build --source <localFeed>" fails with:
        //      error NU1100: Unable to resolve 'CKt.Core (>= 1.0.0)' for 'net8.0'.
        //      PackageSourceMapping is enabled, the following source( s ) were not considered: <localFeed>
        //
        // Even when nuget.config doesn't contain any <packageSourceMapping>...
        // 
        // Before building, we modify the nuget.config (this helper ensures that <packageSourceMapping> exists
        // with all the existing sources plus the one we add.
        //
        var nugetConfigPath = Repo.WorkingFolder.AppendPart( "nuget.config" );
        var nugetConfig = XDocument.Load( nugetConfigPath );
        NuGetHelper.SetOrRemoveNuGetSource( monitor, nugetConfig, "local-feed", localFeed );
        XmlHelper.SaveWithoutXmlDeclaration( nugetConfig, nugetConfigPath );

        return BuildPlugin.RunDotnet( monitor, Repo, $"""
            build -tl:off --nologo --no-incremental -c {(release ? "Release" : "Debug")} /p:Version={version};InformationalVersion="{informationalVersion}";FileVersion="{fileVersion}"
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


