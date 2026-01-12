using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Linq;
using System.Text;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin : PrimaryPluginBase
{
    readonly VersionTagPlugin _versionTags;
    readonly BranchModelPlugin _branchModel;
    readonly RepositoryBuilderPlugin _repoBuilder;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;

    public BuildPlugin( PrimaryPluginContext primaryContext,
                        VersionTagPlugin versionTags,
                        BranchModelPlugin branchModel,
                        RepositoryBuilderPlugin repoBuilder,
                        ReleaseDatabasePlugin releaseDatabase,
                        ArtifactHandlerPlugin artifactHandler )
        : base( primaryContext )
    {
        _versionTags = versionTags;
        _branchModel = branchModel;
        _repoBuilder = repoBuilder;
        _releaseDatabase = releaseDatabase;
        _artifactHandler = artifactHandler;
        World.Events.Issue += IssueRequested;
    }

    [Description( "Build-Test-Package and propagates the current Repo/branch if needed." )]
    [CommandPath( "build" )]
    public bool Build( IActivityMonitor monitor,
                       CKliEnv context,
                       [Description( "Specify the branch to build." )]
                       string branchName,
                       [Description( "Don't run tests even if they have never locally run on this commit." )]
                       bool skipTests = false,
                       [Description( "Run tests even if they have already run successfully on this commit." )]
                       bool forceTests = false,
                       [Description( "Build even if a version tag exists and its artifacts already exist locally." )]
                       bool rebuild = false )
    {
        BranchName? namedBranch;
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest )
            || (namedBranch = _branchModel.GetValidBranchName( monitor, branchName )) == null
            || !_branchModel.CheckBasicPreconditions( monitor, "building", out var allRepos ) )
        {
            return false;
        }
        bool success = true;
        foreach( var repo in allRepos )
        {
        }
        return success;

    }

    [Description( "Build-Test-Package and propagates the current Repo/branch if needed." )]
    [CommandPath( "repo build" )]
    public bool RepoBuild( IActivityMonitor monitor,
                           CKliEnv context,
                           [Description( "Specify the branch to build. By default, the current head is considered." )]
                           [OptionName( "--branch" )]
                           string? branchName = null,
                           [Description( "Don't run tests even if they have never locally run on this commit." )]
                           bool skipTests = false,
                           [Description( "Run tests even if they have already run successfully on this commit." )]
                           bool forceTests = false,
                           [Description( "Build even if a version tag exists and its artifacts already exist locally." )]
                           bool rebuild = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest )
            || !_branchModel.CheckBasicPreconditions( monitor, "building", out var allRepos ) )
        {
            return false; 
        }
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null )
        {
            return false;
        }
        Throw.DebugAssert( "Preconditions fulfilled.", !repo.GitStatus.IsDirty );
        var r = repo.GitRepository.Repository;
        Branch? branch;
        if( branchName != null )
        {
            branch = repo.GitRepository.GetBranch( monitor, branchName, CK.Core.LogLevel.Error );
            if( branch == null )
            {
                return false;
            }
        }
        else
        {
            branch = r.Head;
        }
        branchName = branch.FriendlyName;

        if( BranchModelPlugin.TryParseBranchFixName( branchName, out bool devFix, out var fixMajor, out var fixMinor ) )
        {
            return BuildFix( monitor, repo, r, context, branch, branchName, devFix, fixMajor, fixMinor, runTest, rebuild );
        }
        // We are not on a fix/vMajor.Minor branch.
        // We must be on a branch defined in the Branch Model.
        var branchInfo = _branchModel.Get( monitor, repo );
        Throw.DebugAssert( "There's no branch issue.", branchInfo.Root.GitBranch != null );
        // If we are not on a known branch (defined by the Branch Model), give up.
        var namedBranch = _branchModel.GetValidBranchName( monitor, branchName );
        if( namedBranch == null )
        {
            return false;
        }
        Throw.NotSupportedException( "Not implemented yet." );
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

    bool BuildFix( IActivityMonitor monitor,
                   Repo repo,
                   Repository r,
                   CKliEnv context,
                   Branch branch,
                   string branchName,
                   bool isDevBranch,
                   int fixMajor,
                   int fixMinor,
                   bool? runTest,
                   bool rebuild )
    {
        var versionInfo = _versionTags.Get( monitor, repo );
        var lastFix = versionInfo.LastMajorMinorStables.FirstOrDefault( tc => tc.Version.Major == fixMajor && tc.Version.Minor == fixMinor );
        if( lastFix == null )
        {
            monitor.Error( $"Unable to find any stable version 'v{fixMajor}.{fixMinor}.X' in '{repo.DisplayPath}'." );
            return false;
        }
        Throw.DebugAssert( "See Preconditions: there should be no version tag issue and a missing version tag content is an issue.",
                           lastFix.BuildContentInfo != null );

        // Defensive programming here: the RepoReleaseInfo must exist.
        // This is the starting point of the FixPropagator.
        var originReleaseInfo = _releaseDatabase.GetReleaseInfo( monitor, repo, lastFix.Version );
        if( originReleaseInfo == null )
        {
            monitor.Error( ActivityMonitor.Tags.ToBeInvestigated,
                $"Release '{repo.DisplayPath}/{lastFix.Version}' cannot be found in the Release database and there's no Tag issue. This should not happen." );
            return false;
        }

        var divergence = r.ObjectDatabase.CalculateHistoryDivergence( lastFix.Commit, branch.Tip );
        if( divergence.CommonAncestor.Sha != lastFix.Sha )
        {
            monitor.Error( $"Branch '{branchName}' doesn't contain '{lastFix.Version.ParsedText}' code base that is the last released version." );
            return false;
        }
        // The commit that is considered to be built.
        // May not be the current repository head (but it has the same content).
        Commit? buildCommit = null;
        SVersion? targetVersion = null;

        bool isBranchOnLastFix = branch.Tip.Sha == lastFix.Sha;
        if( isBranchOnLastFix || branch.Tip.Tree.Sha == lastFix.ContentSha )
        {
            // The commit referenced by branch already contains the lastFix code: there's nothing new to build,
            // the target version (if we eventually rebuild) is the lastFix version.
            targetVersion = lastFix.Version;

            // If the lastFix tag is valid (it contains the consumed and produced packages document),
            // and we can find the produced packages in the local NuGet feed, then rebuilding this version
            // is useless. To explicitly rebuild an already built version, we demand the --rebuild flag to be specified.
            var existingContent = lastFix.BuildContentInfo;
            bool isBuildUseless = existingContent != null
                                    ? _artifactHandler.HasAllArtifacts( monitor, repo, lastFix.Version, existingContent )
                                    : false;
            if( isBuildUseless && !rebuild )
            {
                monitor.Error( $"""
                        The commit has already been released in version '{lastFix.Version.ParsedText}' and all artifacts exist in /$Local folder:
                        {existingContent}

                        Use --rebuild flag to rebuild the same version.
                        """ );
                return false;
            }
            if( !isBranchOnLastFix )
            {
                // The branch is on a commit with the same content but not on the lastFix commit.
                // We consider the lastFix.Commit as the buildCommit (instead of the branch's tip that will be the head's tip)
                // to preserve the sha and commit date.
                buildCommit = lastFix.Commit;
            }
        }
        else
        {
            rebuild = false;
            if( isDevBranch )
            {
                int? commitNumber = divergence.BehindBy;
                Throw.DebugAssert( "Because lastFix is the common ancestor.", commitNumber != null );
                Throw.DebugAssert( lastFix.Version is CSVersion );
                var ciBuild = new CIBuildDescriptor { BranchName = "dev", BuildIndex = commitNumber.Value };
                targetVersion = SVersion.Parse( ((CSVersion)lastFix.Version).ToString( CSVersionFormat.Normalized, ciBuild ) );
            }
            else
            {
                targetVersion = SVersion.Create( fixMajor, fixMinor, lastFix.Version.Patch + 1 );
            }
        }
        // Wherever we are, it's time to checkout the working folder on the branch's tip.
        // We also do this when buildCommit is set to the lastFix commit (rebuild case) to avoid
        // the detached head state. 
        if( !repo.GitRepository.Checkout( monitor, branch ) )
        {
            return false;
        }
        // If we are not rebuilding an existing tagged commit, the buildCommit is now the head's tip.
        buildCommit ??= r.Head.Tip;

        // We are ready to build the origin fix.
        var result = CoreBuild( monitor, context, versionInfo, buildCommit, targetVersion, runTest, rebuild );
        if( result == null )
        {
            return false;
        }
        // We create a FixPropagator even if there is no consumer behind (no consumer is not the nominal case) to centralize
        // the checks and the initialization code on the origin/originResult pair.
        var fixer = FixPropagator.Create( monitor, originReleaseInfo, result, context, this, _releaseDatabase, _versionTags );
        if( fixer == null )
        {
            return false;
        }
        return fixer.RunAll( monitor );
    }

    BuildResult? CoreBuild( IActivityMonitor monitor,
                            CKliEnv context,
                            VersionTagInfo versionInfo,
                            Commit buildCommit,
                            SVersion targetVersion,
                            bool? runTest,
                            bool rebuild )
    {
        var buildInfo = versionInfo.TryGetCommitBuildInfo( monitor, buildCommit, targetVersion, rebuild );
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
        var result = DoCoreBuild( monitor, context, _repoBuilder, _releaseDatabase, versionInfo, buildInfo, runTest );
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

        static BuildResult? DoCoreBuild( IActivityMonitor monitor,
                                         CKliEnv context,
                                         RepositoryBuilderPlugin repoBuilder,
                                         ReleaseDatabasePlugin releaseDatabase,
                                         VersionTagInfo versionInfo,
                                         CommitBuildInfo buildInfo,
                                         bool? runTest )
        {
            var buildResult = repoBuilder.Get( monitor, versionInfo.Repo ).Build( monitor, buildInfo, runTest );
            if( buildResult != null )
            {
                var content = buildResult.Content;
                if( !releaseDatabase.OnLocalBuild( monitor, buildResult.Repo, buildResult.Version, buildInfo.Rebuilding, content ) )
                {
                    return null;
                }
                if( !buildInfo.ApplyReleaseBuildTag( monitor, context, content.ToString() ) )
                {
                    return null;
                }
            }
            return buildResult;
        }
    }

    internal static bool RunDotnet( IActivityMonitor monitor, Repo repo, string args, StringBuilder? stdOut = null )
    {
        using( monitor.OpenInfo( $"Executing 'dotnet {args}' in '{repo.DisplayPath}'." ) )
        {
            var e = ProcessRunner.RunProcess( monitor.ParallelLogger,
                                              "dotnet",
                                              args,
                                              repo.WorkingFolder,
                                              stdOut: stdOut );
            if( e != 0 )
            {
                monitor.CloseGroup( $"Failed with code '{e}'." );
                return false;
            }
        }
        return true;
    }

}
