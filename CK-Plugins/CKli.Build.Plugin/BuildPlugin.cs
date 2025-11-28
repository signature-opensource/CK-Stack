using CK.Core;
using CKli.Core;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CKli.VersionTag.Plugin;
using CKli.BranchModel.Plugin;
using LibGit2Sharp;
using CSemVer;
using CKli.LocalNuGetFeed.Plugin;
using System.Text.Json;
using System.Text;
using System;

namespace CKli.Build.Plugin;

public sealed class BuildPlugin : PrimaryPluginBase
{
    readonly VersionTagPlugin _versionTags;
    readonly BranchModelPlugin _branchModel;
    readonly RepositoryBuilderPlugin _repoBuilder;
    readonly LocalNuGetFeedPlugin _localNuGetFeed;

    public BuildPlugin( PrimaryPluginContext primaryContext,
                        VersionTagPlugin versionTags,
                        BranchModelPlugin branchModel,
                        RepositoryBuilderPlugin repoBuilder,
                        LocalNuGetFeedPlugin localNuGetFeed )
        : base( primaryContext )
    {
        _versionTags = versionTags;
        _branchModel = branchModel;
        _repoBuilder = repoBuilder;
        _localNuGetFeed = localNuGetFeed;
    }

    [Description( "Build-Test-Package the current Repo if needed." )]
    [CommandPath( "repo build" )]
    public bool RepoBuild( IActivityMonitor monitor,
                           CKliEnv context,
                           [Description( "Specify the branch to build. By default, the current head is considered." )]
                           [OptionName( "--branch" )]
                           string? branchName,
                           [Description( "Don't run tests even if they have never run on this commit." )]
                           bool skipTests,
                           [Description( "Run tests even if they have already run successfuly on this commit." )]
                           bool forceTests,
                           [Description( "Build even if a version tag already exist." )]
                           bool rebuild )
    {
        bool? runTest = null;
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
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null || !repo.GitRepository.CheckCleanCommit( monitor ) )
        {
            return false;
        }
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
            branchName = branch.FriendlyName;
        }

        if( BranchModelPlugin.TryParseBranchFixName( branchName, out var fixMajor, out var fixMinor ) )
        {
            return BuildFix( monitor, repo, r, context, branch, branchName, fixMajor, fixMinor, runTest, rebuild );
        }
        var branchInfo = _branchModel.Get( monitor, repo );
        if( branchInfo.Root.Branch == null )
        {
            monitor.Error( $"No root branch '{_branchModel.BranchTree.Root.Name}' exists. This must be fixed." );
            return false;
        }
        if( !_branchModel.BranchTree.Branches.TryGetValue( branchName, out var exists ) )
        {
            var branchNames = _branchModel.BranchTree.Branches.Values.Select( b => b.Name );
            monitor.Error( $"""
                Invalid branch '{branchName}'.
                Supported branches are '{branchNames.Order().Concatenate( "', '" )}'.
                """ );
            return false;
        }
        var versionInfo = _versionTags.Get( monitor, repo );

        return true;
    }

    bool BuildFix( IActivityMonitor monitor,
                   Repo repo,
                   Repository r,
                   CKliEnv context,
                   Branch branch,
                   string branchName,
                   int fixMajor,
                   int fixMinor,
                   bool? runTest,
                   bool rebuild )
    {
        var versionInfo = _versionTags.Get( monitor, repo );
        var lastFix = versionInfo.LastStables.Where( tc => tc.Version.Major == fixMajor && tc.Version.Minor == fixMinor ).Max();
        if( lastFix == null )
        {
            monitor.Error( $"Unable to find any stable version 'v{fixMajor}.{fixMinor}.X' in '{repo.DisplayPath}'." );
            return false;
        }
        if( versionInfo.FindFirst( branch.Commits, out _ ) != lastFix )
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
            // is useless. To explicitely rebuild an already built version, we demand the --rebuild flag to be specified.
            bool isBuildUseless = true;
            if( isBuildUseless && !rebuild )
            {
                monitor.Error( $"""
                        The commit has already been released in version '{lastFix.Version.ParsedText}'.
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
            targetVersion = SVersion.Create( fixMajor, fixMinor, lastFix.Version.Patch + 1 );
        }
        // Wherever we are, it's time to checkout the working folder on the branch's tip.
        // We also do this when buildCommit is set to the lastFix commit (rebuild case) to avoid
        // the detached head state. 
        if( branch != r.Head && !repo.SetCurrentBranch( monitor, branchName, skipPullMerge: true ) )
        {
            return false;
        }
        // If we are not rebuilding an existing tagged commit, the buildCommit is now the head's tip.
        buildCommit ??= r.Head.Tip;

        // We are ready to build a fix.
        var buildInfo = versionInfo.TryGetCommitBuildInfo( monitor, buildCommit, targetVersion, rebuild );
        if( buildInfo == null )
        {
            return false;
        }

        var buildResult = _repoBuilder.Get( monitor, repo ).Build( monitor, buildInfo, release: true, runTest );
        if( !buildResult.Success )
        {
            return false;
        }
        try
        {
            // TODO: Generalizes the Artifacts management.
            //       Goal: Handle .zip and/or .exe (setup) files.
            //       Questions: can folders be supported?
            //                  should we enforce the version number in the asset file name (that sounds required).
            // 
            // Publishes the build result artifacts to the local feed: this provides the list of NuGet packages produced.
            var producedPackages = buildResult.PublishToLocalNuGetFeed( monitor, _localNuGetFeed );
            if( producedPackages == null )
            {
                return false;
            }
            var message = new StringBuilder();
            NuGetReleaseInfo.AppendMessage( message, buildResult.ConsumedPackages, producedPackages );

            if( !buildInfo.ApplyRealeaseBuildTag( monitor, context, message.ToString() ) )
            {
                return false;
            }

            // TODO: Propagate this build where it must be propagated...

        }
        finally
        {
            buildResult.CleanupTemporaryFolder( monitor );
        }
        return true;
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
