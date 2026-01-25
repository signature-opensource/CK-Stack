using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli.Build.Plugin;

/// <summary>
/// Concrete base class that can be specialized.
/// <para>
/// The <see cref="RepositoryBuilderPlugin.Create(IActivityMonitor, Repo)"/> acts as an abstract factory:
/// the actual RepoBuilder may differ for each Repo one day.
/// </para>
/// </summary>
public class RepoBuilder : RepoInfo
{
    readonly RepositoryBuilderPlugin _repositoryBuilder;
    readonly RepoArtifactInfo _repoArtifact;

    public RepoBuilder( Repo repo, RepositoryBuilderPlugin repositoryBuilder, RepoArtifactInfo repoArtifact )
        : base( repo )
    {
        _repositoryBuilder = repositoryBuilder;
        _repoArtifact = repoArtifact;
    }

    /// <summary>
    /// Checks whether tests has already successfully run on the commit.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="commit">The commit.</param>
    /// <returns>True if the tests have already successfully run, false otherwise.</returns>
    public bool HasTestRun( IActivityMonitor monitor, Commit commit )
    {
        string testKey = commit.Tree.Sha;
        Throw.DebugAssert( _repositoryBuilder._shaTestRunCache != null );
        return _repositoryBuilder._shaTestRunCache.Contains( monitor, testKey );
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
    public async Task<BuildResult?> BuildAsync( IActivityMonitor monitor,
                                                CommitBuildInfo buildInfo,
                                                bool runTest )
    {
        Throw.CheckState( "Repository must not be dirty when calling build.",
                          !Repo.GitRepository.GetSimpleStatusInfo().IsDirty );
        Throw.CheckArgument( buildInfo.Repo == Repo );

        // Calling "dotnet build --source <localFeed>" fails with:
        //      error NU1100: Unable to resolve 'CKt.Core (>= 1.0.0)' for 'net8.0'.
        //      PackageSourceMapping is enabled, the following source( s ) were not considered: <localFeed>
        //
        // Even when nuget.config doesn't contain any <packageSourceMapping>...
        // 
        // Before building, we modify the nuget.config (this helper ensures that <packageSourceMapping> exists
        // with all the existing sources plus the one we add).
        //
        monitor.Trace( "Add the local feed to the 'nuget.config' file." );
        var localFeed = _repoArtifact.LocalFeedNuGetPath;
        var nugetConfigPath = Repo.WorkingFolder.AppendPart( "nuget.config" );
        var nugetConfig = XDocument.Load( nugetConfigPath );
        NuGetHelper.SetOrRemoveNuGetSource( monitor, nugetConfig, "local-feed", localFeed );
        XmlHelper.SaveWithoutXmlDeclaration( nugetConfig, nugetConfigPath, SaveOptions.DisableFormatting );
        // We ResetHard the repository after the build.
        bool resetHardDone = false;

        // Temporary output folder for artifacts.
        var outputPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath() + "CKliBuild", null, DateTime.UtcNow );

        try
        {

            if( await _repositoryBuilder.RaiseOnCoreBuildAsync( monitor, buildInfo ).ConfigureAwait( false )
                && DotNetBuildTestPack( monitor, buildInfo, runTest, outputPath ) )
            {
                // If we cannot reset the working folder, we don't want to ignore this at all: the build fails.
                if( !Repo.GitRepository.ResetHard( monitor, out _, tryDeleteUntrackedFiles: true) )
                {
                    monitor.Error( $"Unable to reset the working folder after build. This should not happen: failing the build." );
                    return null;
                }
                resetHardDone = true;
                if( BuildResult.GetConsumedPackages( monitor, Repo, buildInfo.ToString(), out var consumedPackages ) )
                {
                    var deploymentFolder = Repo.WorkingFolder.AppendPart( ArtifactHandlerPlugin.DeployFolderName );
                    if( HandleDeployAssets( monitor, deploymentFolder, buildInfo.Version, out var assetsFolder, out var assetFileNames )
                        && _repoArtifact.PublishToNuGetLocalFeed( monitor, buildInfo.Version, outputPath, out var publishedPackages ) ) 
                    {
                        var r = new BuildResult( Repo,
                                                 buildInfo.Version,
                                                 consumedPackages,
                                                 publishedPackages,
                                                 assetsFolder,
                                                 assetFileNames );

                        return r;
                    }
                }
            }
        }
        finally
        {
            if( outputPath != null ) FileHelper.DeleteFolder( monitor, outputPath );
            // If reset has not been done (build failed), do it.
            if( !resetHardDone )
            {
                Repo.GitRepository.ResetHard( monitor, out var remainingUntrackedFiles, tryDeleteUntrackedFiles: true );
            }
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
    /// This raises the <see cref="RepositoryBuilderPlugin.OnCoreBuild"/> and calls the <see cref="DotNetBuildTestPack"/> helper.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="buildInfo">The build information.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected virtual async Task<bool> DoBuildAsync( IActivityMonitor monitor,
                                                     CommitBuildInfo buildInfo,
                                                     bool runTest,
                                                     NormalizedPath outputPath )
    {
        return await _repositoryBuilder.RaiseOnCoreBuildAsync( monitor, buildInfo ).ConfigureAwait( false )
               && DotNetBuildTestPack( monitor, buildInfo, runTest, outputPath );
    }

    /// <summary>
    /// Helper that calls <see cref="DotNetBuild"/>, <see cref="DotNetTest"/> and <see cref="DotNetPack"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="buildInfo">The build information.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <param name="packOutputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected virtual bool DotNetBuildTestPack( IActivityMonitor monitor,
                                                CommitBuildInfo buildInfo,
                                                bool runTest,
                                                string packOutputPath )
    {
        return DotNetBuild( monitor,
                            buildInfo.Version,
                            buildInfo.InformationalVersion,
                            buildInfo.FileVersion,
                            buildInfo.ReleaseConfiguration )
               && (!runTest || DotNetTest( monitor ))
               && DotNetPack( monitor, buildInfo.Version, buildInfo.ReleaseConfiguration, packOutputPath );
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
        return Repo.RunDotnet( monitor, $"""
            build -tl:off --nologo --no-incremental -c {(release ? "Release" : "Debug")} /p:Version={version};InformationalVersion="{informationalVersion}";FileVersion="{fileVersion}"
            """ );
    }

    /// <summary>
    /// Helper that calls "dotnet test".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on tests success, false otherwise.</returns>
    protected bool DotNetTest( IActivityMonitor monitor )
    {
        if( !Repo.RunDotnet( monitor, $"test -tl:off --nologo --no-build" ) )
        {
            return false;
        }
        Throw.DebugAssert( _repositoryBuilder._shaTestRunCache != null );
        var testCache = _repositoryBuilder._shaTestRunCache;
        string testKey = Repo.GitRepository.Repository.Head.Tip.Tree.Sha;
        testCache.Add( monitor, testKey );
        return true;
    }

    /// <summary>
    /// Helper that calls "dotnet pack".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to pack.</param>
    /// <param name="release">Whether the build is in debug or in release.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetPack( IActivityMonitor monitor, SVersion version, bool release, string outputPath )
    {
        return Repo.RunDotnet( monitor, $"""
            pack -tl:off /p:Version={version} -c {(release ? "Release" : "Debug")} --nologo --no-build -o "{outputPath}" 
            """ );
    }


}


