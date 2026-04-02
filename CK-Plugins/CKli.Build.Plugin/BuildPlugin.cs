using CK.Core;
using CK.PerfectEvent;
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
    const string _descBranch = "Specify the branch to consider. By default, the current head is considered when in a Repo.";
    const string _descMaxDoP = "Maximal Degree of Parallelism. Defaults to 4.";
    const string _descDryRun = "Only display the build roadmap.";
    const string _descBuildPublish = "On success, publish the generated packages and asset files.";
    const string _descNoPublish = "Don't publish the generated packages and asset files.";

    readonly VersionTagPlugin _versionTags;
    readonly BranchModelPlugin _branchModel;
    readonly RepositoryBuilderPlugin _repoBuilder;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ShallowSolutionPlugin _solutionPlugin;
    readonly PerfectEventSender<RoadmapBuildEventArgs> _onRoadmapBuild;
    readonly PerfectEventSender<FixBuildEventArgs> _onFixBuild;


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
        _onRoadmapBuild = new PerfectEventSender<RoadmapBuildEventArgs>();
        _onFixBuild = new PerfectEventSender<FixBuildEventArgs>();
    }

    /// <summary>
    /// Raised whenever a <see cref="Roadmap"/> has been successfully built.
    /// </summary>
    public PerfectEvent<RoadmapBuildEventArgs> OnRoadmapBuild => _onRoadmapBuild.PerfectEvent;

    /// <summary>
    /// Raised whenever a fix has been successfully built.
    /// </summary>
    public PerfectEvent<FixBuildEventArgs> OnFixBuild => _onFixBuild.PerfectEvent;

    [Description( "Build-Test-Package and propagates packages from the current repositories to their consumers." )]
    [CommandPath( "build" )]
    public Task<bool> Build( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description( _descBranch )]
                             [OptionName("--branch,-b")]
                             string? branch = null,
                             [Description( _descMaxDoP )]
                             string? maxDop = null,
                             [Description( "Build all the Repos, not only the current repositories and their consumers." )]
                             bool all = false,
                             [Description( "Don't run tests even if they have never locally run on the commit." )]
                             bool skipTests = false,
                             [Description( "Run tests even if they have already run successfully on the commit." )]
                             bool forceTests = false,
                             [Description( _descBuildPublish )]
                             [OptionName("--publish")]
                             bool publish = false,
                             [Description( _descDryRun )]
                             [OptionName("--dry-run,-d")]
                             bool dryRun = false )
    {
        return DoBuildAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, publish, dryRun, isPullBuild: false );
    }

    [Description( "Build-Test-Package the consumers of the current repositories and propagates packages to their consumers." )]
    [CommandPath( "*build" )]
    public Task<bool> StarBuild( IActivityMonitor monitor,
                                 CKliEnv context,
                                 [Description( _descBranch )]
                                 [OptionName("--branch,-b")]
                                 string? branch = null,
                                 [Description( _descMaxDoP )]
                                 string? maxDop = null,
                                 [Description( "Build all the Repos, not only the ones that consume or produce the current repositories." )]
                                 bool all = false,
                                 [Description( "Don't run tests even if they have never locally run on the commit." )]
                                 bool skipTests = false,
                                 [Description( "Run tests even if they have already run successfully on the commit." )]
                                 bool forceTests = false,
                                 [Description( _descBuildPublish )]
                                 [OptionName("--publish")]
                                 bool publish = false,
                                 [Description( _descDryRun )]
                                 [OptionName("--dry-run,-d")]
                                 bool dryRun = false )
    {
        return DoBuildAsync( monitor, context, branch, maxDop, all, skipTests, forceTests, publish, dryRun, isPullBuild: true );
    }

    [Description( "Build-Test-Package and propagates packages from the current repositories to their consumers and publishes all the artifacts." )]
    [CommandPath( "publish" )]
    public Task<bool> Publish( IActivityMonitor monitor,
                               CKliEnv context,
                               [Description( _descBranch )]
                               [OptionName( "--branch,-b" )]
                               string? branch = null,
                               [Description( _descMaxDoP )]
                               string? maxDop = null,
                               [Description( "Build all the Repos, not only the current repositories and their consumers." )]
                               bool all = false,
                               [Description( "Run tests even if they have already run successfully on the commit." )]
                               bool forceTests = false,
                               [Description( _descNoPublish )]
                               [OptionName("--no-publish")]
                               bool noPublish = false,
                               [Description( _descDryRun )]
                               [OptionName("--dry-run,-d")]
                               bool dryRun = false )
    {
        return DoPublishAsync( monitor, context, branch, maxDop, all, forceTests, dryRun, isPullBuild: false, shouldPublish: !noPublish );
    }

    [Description( "Build-Test-Package the consumers of the current repositories, propagates packages to their consumers and publishes all the artifacts." )]
    [CommandPath( "*publish" )]
    public Task<bool> StarPublish( IActivityMonitor monitor,
                                   CKliEnv context,
                                   [Description( _descBranch )]
                                   [OptionName( "--branch,-b" )]
                                   string? branch = null,
                                   [Description( _descMaxDoP )]
                                   string? maxDop = null,
                                   [Description( "Publish all the Repos, not only the ones that consume or produce the current repositories." )]
                                   bool all = false,
                                   [Description( "Run tests even if they have already run successfully on the commit." )]
                                   bool forceTests = false,
                                   [Description( _descNoPublish )]
                                   [OptionName("--no-publish")]
                                   bool noPublish = false,
                                   [Description( _descDryRun )]
                                   [OptionName("--dry-run,-d")]
                                   bool dryRun = false )
    {
        return DoPublishAsync( monitor, context, branch, maxDop, all, forceTests, dryRun, isPullBuild: true, shouldPublish: !noPublish );
    }

    Task<bool> DoBuildAsync( IActivityMonitor monitor,
                             CKliEnv context,
                             string? branch,
                             string? maxDop,
                             bool all,
                             bool skipTests,
                             bool forceTests,
                             bool publish,
                             bool dryRun,
                             bool isPullBuild )
    {
        if( !HandleMaxDoP( monitor, maxDop, out var vMaxDoP )
            || !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return Task.FromResult( false );
        }
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild, isCIBuild: true, branch, all );
        if( roadmap == null )
        {
            return Task.FromResult( false );
        }
        if( dryRun )
        {
            return Task.FromResult( true );
        }
        return DoRunBuild( monitor, context, publish, vMaxDoP, runTest, roadmap );
    }

    Task<bool> DoPublishAsync( IActivityMonitor monitor,
                               CKliEnv context,
                               string? branch,
                               string? maxDop,
                               bool all,
                               bool forceTests,
                               bool dryRun,
                               bool isPullBuild,
                               bool shouldPublish )
    {
        var roadmap = ComputeAndDisplayRoadmap( monitor, context, isPullBuild, isCIBuild: false, branch, all );
        if( roadmap == null || !HandleMaxDoP( monitor, maxDop, out var vMaDxDop ) )
        {
            return Task.FromResult( false );
        }
        if( dryRun )
        {
            return Task.FromResult( true );
        }
        bool? runTest = forceTests ? true : null;
        return DoRunBuild( monitor, context, shouldPublish, vMaDxDop, runTest, roadmap );
    }

    async Task<bool> DoRunBuild( IActivityMonitor monitor, CKliEnv context, bool publish, int vMaxDoP, bool? runTest, Roadmap roadmap )
    {
        var results = await roadmap.BuildAsync( monitor, context, this, runTest, vMaxDoP ).ConfigureAwait( false );
        if( results == null )
        {
            return false;
        }
        if( results.Length > 0 && _onRoadmapBuild.HasHandlers )
        {
            var e = new RoadmapBuildEventArgs( monitor, roadmap, publish );
            return await _onRoadmapBuild.SafeRaiseAsync( monitor, e ).ConfigureAwait( false );
        }
        return true;
    }


    Roadmap? ComputeAndDisplayRoadmap( IActivityMonitor monitor,
                                       CKliEnv context,
                                       bool isPullBuild,
                                       bool isCIBuild,
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
            branch = GetBranchName( monitor, pivots[0] );
            for( int  i = 1; i < pivots.Count; ++i )
            {
                var bOther = GetBranchName( monitor, pivots[i] );
                if( bOther != branch )
                {
                    monitor.Error( $"""
                                     Multiple Repo are selected and current checked out branches differ, the --branch <name> must be specified.
                                     (At least, '{pivots[0].DisplayPath}' is on '{branch}' and '{pivots[i].DisplayPath}' is on '{bOther}'.)
                                     """ );
                    return null;
                }
            }
            monitor.Info( ScreenType.CKliScreenTag, $"Considering current branch '{branch}' as the --branch <name> to build." );
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

        // Computes the PackageUpdater. This is at the level of the HotGraph and handles:
        // - Existing built versions for packages produced by the World.
        // - The VersionTagPlugin World's configuration packages.
        // - And the external package discrepancies across the World.
        //
        // At this stage, this cannot contain the target build versions of the Roadmap: this is why
        // the Roadmap uses a PackageMapping that wraps the HotGraph package mappings to first consider the target versions.
        //
        // (Introducing this layer enables the build process to rely on this PackageMapping as a read only structure that
        // is de facto concurrent safe: before April 2026, a ConcurrentDictionary was used as a layer above the
        // PackageUpdater.Mappings that was updated by the RoadmapExecutor.DoBuildAsync after each build.)
        //
        var packageUpdater = hotGraph.GetPackageUpdater( monitor );
        if( packageUpdater == null ) return null;

        var roadmap = new Roadmap( _versionTags, hotGraph, packageUpdater, isPullBuild, isCIBuild );
        if( !roadmap.Initialize( monitor ) )
        {
            return null;
        }
        context.Screen.Display( roadmap.ToRenderable );
        return roadmap;

        static string GetBranchName( IActivityMonitor monitor, Repo r )
        {
            string branch = r.GitStatus.CurrentBranchName;
            if( branch.StartsWith( "dev/", StringComparison.OrdinalIgnoreCase ) )
            {
                branch = branch.Substring( 4 );
            }
            return branch;
        }
    }

    static bool HandleMaxDoP( IActivityMonitor monitor, string? maxDop, out int vMaxDop )
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
