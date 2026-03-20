using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.IO;
using System.Threading.Tasks;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin : PrimaryPluginBase
{
    readonly VersionTagPlugin _versionTags;
    readonly BranchModelPlugin _branchModel;
    readonly RepositoryBuilderPlugin _repoBuilder;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ShallowSolutionPlugin _solutionPlugin;

    public BuildPlugin( PrimaryPluginContext primaryContext,
                        VersionTagPlugin versionTags,
                        BranchModelPlugin branchModel,
                        RepositoryBuilderPlugin repoBuilder,
                        ReleaseDatabasePlugin releaseDatabase,
                        ArtifactHandlerPlugin artifactHandler,
                        ShallowSolutionPlugin solutionPlugin )
        : base( primaryContext )
    {
        _versionTags = versionTags;
        _branchModel = branchModel;
        _repoBuilder = repoBuilder;
        _releaseDatabase = releaseDatabase;
        _artifactHandler = artifactHandler;
        _solutionPlugin = solutionPlugin;
        World.Events.Issue += IssueRequested;
    }

    [Description( "Build-Test-Package and propagates packages from the current repositories to their consumers." )]
    [CommandPath( "build" )]
    public Task<bool> BuildStar( IActivityMonitor monitor,
                                 CKliEnv context,
                                 [Description( "Specify the branch to build. By default, the current head is considered when in a Repo." )]
                                 [OptionName("--branch,-b")]
                                 string? branch = null,
                                 [Description( "Maximal Degree of Parallelism. Defaults to 4." )]
                                 string? maxDop = null,
                                 [Description( "Build all the Repos, not only the current repositories and their consumers." )]
                                 bool all = false,
                                 [Description( "Don't run tests even if they have never locally run on the commit." )]
                                 bool skipTests = false,
                                 [Description( "Run tests even if they have already run successfully on the commit." )]
                                 bool forceTests = false,
                                 [Description( "Only display the build roadmap." )]
                                 [OptionName("--dry-run,-d")]
                                 bool dryRun = false )
    {
        if( !HandleMaxDop( monitor, maxDop, out var vMaDxDop )
            || !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return Task.FromResult( false );
        }
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild: false, isDevBuild: true, branch, all );
        if( roadmap == null )
        {
            return Task.FromResult( false );
        }
        return dryRun
                ? Task.FromResult( true )
                : roadmap.BuildAsync( monitor, context, this, runTest, vMaDxDop );
    }

    [Description( "Build-Test-Package the consumers of the current repositories and propagates packages to their consumers." )]
    [CommandPath( "*build" )]
    public Task<bool> StarBuildStar( IActivityMonitor monitor,
                                     CKliEnv context,
                                     [Description( "Specify the branch to build. By default, the current head is considered when in a Repo." )]
                                     [OptionName("--branch,-b")]
                                     string? branch = null,
                                     [Description( "Maximal Degree of Parallelism. Defaults to 4." )]
                                     string? maxDop = null,
                                     [Description( "Build all the Repos, not only the ones that consume or produce the current repositories." )]
                                     bool all = false,
                                     [Description( "Don't run tests even if they have never locally run on the commit." )]
                                     bool skipTests = false,
                                     [Description( "Run tests even if they have already run successfully on the commit." )]
                                     bool forceTests = false,
                                     [Description( "Only display the build roadmap." )]
                                     [OptionName("--dry-run,-d")]
                                     bool dryRun = false )
    {
        if( !HandleMaxDop( monitor, maxDop, out var vMaDxDop )
            || !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return Task.FromResult( false );
        }
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild: true, isDevBuild: true, branch, all );
        if( roadmap == null )
        {
            return Task.FromResult( false );
        }
        return dryRun
                ? Task.FromResult( true )
                : roadmap.BuildAsync( monitor, context, this, runTest, vMaDxDop );
    }

    [Description( "Build-Test-Package and propagates packages from the current repositories to their consumers and publishes all the artifacts." )]
    [CommandPath( "publish" )]
    public Task<bool> PublishStar( IActivityMonitor monitor,
                                   CKliEnv context,
                                   [Description( "Specify the branch to publish. By default, the current head is considered when in a Repo." )]
                                   [OptionName( "--branch,-b" )]
                                   string? branch = null,
                                   [Description( "Maximal Degree of Parallelism. Defaults to 4." )]
                                   string? maxDop = null,
                                   [Description( "Build all the Repos, not only the current repositories and their consumers." )]
                                   bool all = false,
                                   [Description( "Run tests even if they have already run successfully on the commit." )]
                                   bool forceTests = false,
                                   [Description( "Only display the build roadmap." )]
                                   [OptionName("--dry-run,-d")]
                                   bool dryRun = false )
    {
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild: false, isDevBuild: false, branch, all );
        if( roadmap == null || !HandleMaxDop( monitor, maxDop, out var vMaDxDop ) )
        {
            return Task.FromResult( false );
        }
        if( dryRun ) return Task.FromResult( true );
        bool? runTest = forceTests ? true : null;
        throw new NotImplementedException();
    }

    [Description( "Build-Test-Package the consumers of the current repositories, propagates packages to their consumers and publishes all the artifacts." )]
    [CommandPath( "*publish" )]
    public Task<bool> StarPublishStar( IActivityMonitor monitor,
                                       CKliEnv context,
                                       [Description( "Specify the branch to publish. By default, the current head is considered when in a Repo." )]
                                       [OptionName( "--branch,-b" )]
                                       string? branch = null,
                                       [Description( "Maximal Degree of Parallelism. Defaults to 4." )]
                                       string? maxDop = null,
                                       [Description( "Publish all the Repos, not only the ones that consume or produce the current repositories." )]
                                       bool all = false,
                                       [Description( "Run tests even if they have already run successfully on the commit." )]
                                       bool forceTests = false,
                                       [Description( "Only display the build roadmap." )]
                                       [OptionName("--dry-run,-d")]
                                       bool dryRun = false )
    {
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild: true, isDevBuild: false, branch, all );
        if( roadmap == null || !HandleMaxDop( monitor, maxDop, out var vMaDxDop ) )
        {
            return Task.FromResult( false );
        }
        if( dryRun ) return Task.FromResult( true );
        bool? runTest = forceTests ? true : null;
        throw new NotImplementedException();
    }

    Roadmap? ComputeAndDisplayRoadmap( IActivityMonitor monitor,
                                       CKliEnv context,
                                       bool isPullBuild,
                                       bool isDevBuild,
                                       string? branch,
                                       bool all )
    {
        // Consider the repositories selected by current path as the Pivots.
        var pivots = all
                        ? World.GetAllDefinedRepo( monitor )
                        : World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( pivots == null )
        {
            return null;
        }
        if( branch == null )
        {
            if( pivots.Count > 1 )
            {
                monitor.Error( all ? "When 'ckli build --all' is specified, the --branch <name> must be specified."
                                   : $"""
                                     More than one Repo are below path '{context.CurrentDirectory}'.
                                     The --branch <name> must be specified.
                                     """ );
                return null;
            }
            var r = pivots[0];
            branch = r.GitStatus.CurrentBranchName;
            monitor.Info( ScreenType.CKliScreenTag,
                          $"Considering current branch '{branch}' from '{r.DisplayPath}' as the --branch <name> to build." );
            if( branch.StartsWith( "dev/", StringComparison.OrdinalIgnoreCase ) )
            {
                branch = branch.Substring( 4 );
            }
        }
        // If we are not on a known branch (defined by the Branch Model), give up.
        var branchName = _branchModel.GetValidBranchName( monitor, branch );
        if( branchName == null )
        {
            return null;
        }
        // When --all is specified, all the repositories are pivots and the actual branch name considered by
        // the hot graph will be the most instable one of all the repositories (but at least as stable as the
        // branchName resolved above of course).
        var hotGraph = _branchModel.GetHotGraph( monitor, branchName, pivots );
        if( hotGraph == null ) return null;

        var packageUpdater = hotGraph.GetPackageUpdater( monitor );
        if( packageUpdater == null ) return null;

        var roadmap = new Roadmap( _versionTags, hotGraph, packageUpdater, isPullBuild, isDevBuild );
        if( !roadmap.Initialize( monitor ) )
        {
            return null;
        }
        context.Screen.Display( roadmap.ToRenderable );
        return roadmap;
    }

    static bool HandleMaxDop( IActivityMonitor monitor, string? maxDop, out int vMaxDop )
    {
        if( maxDop == null ) vMaxDop = 4;
        else if( !int.TryParse( maxDop, out vMaxDop ) || vMaxDop <= 0 )
        {
            monitor.Error( "Invalid --max-dop value. Must be an integer greater than 0." );
            return false;
        }
        return true;
    }

    static bool HandleForceSkipTests( IActivityMonitor monitor, bool skipTests, bool forceTests, out bool? runTest )
    {
        runTest = null;
        if( forceTests )
        {
            if( skipTests )
            {
                monitor.Error( $"Invalid flags combination: --skip-test and --force-test cannot be both specified." );
                return false;
            }
            runTest = true;
        }
        else if( skipTests )
        {
            runTest = false;
        }
        return true;
    }

    async Task<BuildResult?> CoreBuildAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             VersionTagInfo versionInfo,
                                             Commit buildCommit,
                                             SVersion targetVersion,
                                             bool? runTest,
                                             bool forceRebuild = false )
    {
        // Obtain the RepoBuilder for the Repo.
        var repoBuilder = _repoBuilder.Get( monitor, versionInfo.Repo );
        // Should we run the tests?
        runTest ??= !repoBuilder.HasTestRun( monitor, buildCommit );

        if( !forceRebuild )
        {
            var existingRelease = _releaseDatabase.GetReleaseInfo( monitor, versionInfo.Repo, targetVersion, LogLevel.Debug );
            if( existingRelease != null && existingRelease.HasAllLocalArtifacts( monitor, out var assetsFolder ) )
            {
                // build is not required... But may be running tests is required.
                if( !runTest.Value )
                {
                    monitor.Info( $"Useless build for '{versionInfo.Repo.DisplayPath}/{targetVersion}' skipped." );
                    return new BuildResult( versionInfo.Repo, targetVersion, existingRelease.Content, assetsFolder );
                }
            }
        }

        var buildInfo = versionInfo.TryGetCommitBuildInfo( monitor, buildCommit, targetVersion, allowRebuild: forceRebuild );
        if( buildInfo == null )
        {
            return null;
        }
        using var gLog = monitor.OpenTrace( $"Core build for '{buildInfo}'." );
        //
        // We ensure that the working folder is checked out on the buildCommit content tree.
        // We restore the current branch once we are done.
        // Note that the Branch Head may be a DetachedHead (internal LibGit2Sharp specialization of a Branch) but
        // we don't care: we restore the current state.
        //
        var git = versionInfo.Repo.GitRepository;
        if( !git.CheckCleanCommit( monitor ) )
        {
            return null;
        }

        Branch currentHead = git.Repository.Head;
        bool mustCheckOut = currentHead.Tip.Tree.Sha != buildCommit.Tree.Sha;
        if( mustCheckOut )
        {
            monitor.Trace( $"Current working folder content is not the same as the commit '{buildCommit.Sha}' to build. Checking out a detached head." );
            Commands.Checkout( git.Repository, buildCommit );
        }
        var result = await DoCoreBuildAsync( monitor,
                                             context,
                                             repoBuilder,
                                             _releaseDatabase,
                                             buildInfo,
                                             runTest.Value ).ConfigureAwait( false );
        if( mustCheckOut )
        {
            try
            {
                monitor.Trace( "Restoring working folder to its previous head." );
                Commands.Checkout( git.Repository, currentHead, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force } );

                // When rebuilding old commit points, ignored artifacts may remains on the file system.
                // We are rather aggressive here and try to cleanup the file system. 
                git.ResetHard( monitor, out var remainingUntrackedFiles, tryDeleteUntrackedFiles: true );

                // It may seem logical here to not include untracked files here (and ask only for ignored ones)
                // but when setting IncludeUntracked and RecurseUntrackedDirs to false here and building CKt-Core/v1.0.0,
                // this removes CKt.Core/bin and /obj but leaves CodeCakeBuilder/bin and /obj...
                // That is PRECISELY what we want to remove :-(.
                // So let IncludeUntracked be true here.
                var status = git.Repository.RetrieveStatus( new StatusOptions
                {
                    DetectRenamesInIndex = false,
                    IncludeIgnored = true
                } );
                foreach( var e in status.Ignored )
                {
                    var eName = e.FilePath;
                    var fullPath = Path.Combine( git.WorkingFolder, eName );
                    if( eName[^1] == '/' )
                    {
                        FileHelper.DeleteFolder( monitor, fullPath );
                    }
                    else
                    {
                        FileHelper.DeleteFile( monitor, fullPath );
                    }
                }

                FileHelper.DeleteEmptyFoldersBelow( monitor, git.WorkingFolder, LogLevel.Warn );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Error while restoring '{git.DisplayPath}' back to '{currentHead}'.", ex );
                result = null;
            }
        }
        return result;

        static async Task<BuildResult?> DoCoreBuildAsync( IActivityMonitor monitor,
                                                          CKliEnv context,
                                                          RepoBuilder repoBuilder,
                                                          ReleaseDatabasePlugin releaseDatabase,
                                                          CommitBuildInfo buildInfo,
                                                          bool runTest )
        {
            var buildResult = await repoBuilder.BuildAsync( monitor, buildInfo, runTest ).ConfigureAwait( false );
            if( buildResult != null )
            {
                var content = buildResult.Content;
                if( !releaseDatabase.OnLocalBuild( monitor, buildResult.Repo, buildResult.Version, buildInfo.Rebuilding, content ) )
                {
                    return null;
                }
                // Local fix builds have no release tag. If the release local database is reset, we lose them
                // but this is not an issue, this is used as an optimization that avoids rebuilding origins when
                // an impacted repo needs to be rebuilt.
                if( !buildResult.Version.IsLocalFix() )
                {
                    if( !buildInfo.ApplyReleaseBuildTag( monitor, context, content.ToString() ) )
                    {
                        return null;
                    }
                }
            }
            return buildResult;
        }
    }

}
