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
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

    [Description( "Build-Test-Package and propagates local packages across the repositories." )]
    [CommandPath( "build" )]
    public bool RepoBuild( IActivityMonitor monitor,
                           CKliEnv context,
                           [Description( "Specify the branch to build. By default, the current head is considered." )]
                           [OptionName( "--branch" )]
                           string? branch = null,
                           [Description( "Build all the Repos, not only the ones that consume or produce the current repository." )]
                           bool all = false,
                           [Description( "Don't run tests even if they have never locally run on this commit." )]
                           bool skipTests = false,
                           [Description( "Run tests even if they have already run successfully on this commit." )]
                           bool forceTests = false,
                           [Description( "Build even if a version tag exists and its artifacts already exist locally." )]
                           bool rebuild = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return false;
        }
        // Consider the repositories selected by current path as the Pivots.
        var pivots = World.GetAllDefinedRepo( monitor, context.CurrentDirectory );
        if( pivots == null || pivots.Count == 0 )
        {
            return false;
        }
        if( branch == null )
        {
            if( all )
            {
                monitor.Error( "When 'ckli build --all' is specified, the --branch <name> must be specified." );
                return false;
            }
            if( pivots.Count > 1 )
            {
                monitor.Error( $"""
                    More than one Repo are below path '{context.CurrentDirectory}'.
                    The --branch <name> must be specified.
                    """ );
                return false;
            }
            var r = pivots.Single();
            branch = r.GitStatus.CurrentBranchName;
            monitor.Trace( $"Considering current branch '{branch}' from '{r.DisplayPath}' as the --branch <name> to build." );
        }
        // If we are not on a known branch (defined by the Branch Model), give up.
        var branchName = _branchModel.GetValidBranchName( monitor, branch );
        if( branchName == null )
        {
            return false;
        }
        // build is for "dev" builds. Otherwise it's called "publish".
        if( !branchName.IsDevBranch )
        {
            branchName = branchName.DevBranch;
        }
        // When --all is specified, all the repositories are pivots and the actual branch name considered by
        // the hot graph will be the most instable one of all the repositories (but at least as stable as the
        // branchName resolved above of course).
        var hotGraph = _branchModel.GetHotGraph( monitor, branchName, all ? [] : pivots );
        if( hotGraph == null )
        {
            return false;
        }
        var roadmap = new Roadmap( hotGraph );
        context.Screen.Display( roadmap.ToRenderable );

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
            var existingRelease = _releaseDatabase.GetReleaseInfo( monitor, versionInfo.Repo, targetVersion );
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
                                             versionInfo,
                                             buildInfo,
                                             runTest.Value ).ConfigureAwait( false );
        if( mustCheckOut )
        {
            try
            {
                monitor.Trace( "Restoring working folder to its previous head." );
                Commands.Checkout( git.Repository, currentHead, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force } );
                FileHelper.DeleteEmptyFoldersBelow( monitor, versionInfo.Repo.WorkingFolder, LogLevel.Warn );
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
                                                          VersionTagInfo versionInfo,
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
                // but this is not issue, this is used as an optimization that avoids rebuilding origins when
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
