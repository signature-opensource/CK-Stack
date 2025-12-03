using CK.Core;
using CKli.Core;
using System.Linq;
using CKli.VersionTag.Plugin;
using CKli.BranchModel.Plugin;
using LibGit2Sharp;
using CSemVer;
using System.Text;
using System;
using CKli.ArtifactHandler.Plugin;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin : PrimaryPluginBase
{
    readonly VersionTagPlugin _versionTags;
    readonly BranchModelPlugin _branchModel;
    readonly RepositoryBuilderPlugin _repoBuilder;
    readonly ArtifactHandlerPlugin _artifactHandler;

    public BuildPlugin( PrimaryPluginContext primaryContext,
                        VersionTagPlugin versionTags,
                        BranchModelPlugin branchModel,
                        RepositoryBuilderPlugin repoBuilder,
                        ArtifactHandlerPlugin artifactHandler )
        : base( primaryContext )
    {
        _versionTags = versionTags;
        _branchModel = branchModel;
        _repoBuilder = repoBuilder;
        _artifactHandler = artifactHandler;
        World.Events.Issue += IssueRequested;
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
                           [Description( "Build even if a version tag exists and its artifacts locally found." )]
                           bool rebuild = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return false;
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
        // We are not on a vMajor.Minor/fix branch.
        // We must be on a branch defined in the Branch Model (if at least the root branch exists in the Repo).
        var branchInfo = _branchModel.Get( monitor, repo );
        if( branchInfo.Root.Branch == null )
        {
            monitor.Error( $"No root branch '{_branchModel.BranchTree.Root.Name}' exists. This must be fixed." );
            return false;
        }
        // If we are not on a known branch (defined by the Branch Model), give up.
        if( !_branchModel.BranchTree.Branches.TryGetValue( branchName, out var exists ) )
        {
            var branchNames = _branchModel.BranchTree.Branches.Values.Select( b => b.Name );
            monitor.Error( $"""
                Invalid branch '{branchName}'.
                Supported branches are '{branchNames.Order().Concatenate( "', '" )}'.
                """ );
            return false;
        }
        Throw.NotSupportedException( "Not implemented yet." );
        return true;
    }

    private static bool HandleForceSkipTests( IActivityMonitor monitor, bool skipTests, bool forceTests, out bool? runTest )
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
            // is useless. To explicitly rebuild an already built version, we demand the --rebuild flag to be specified.
            var existingContent = lastFix.BuildContentInfo;
            bool isBuildUseless = existingContent != null
                                    ? _artifactHandler.HasAllArtifacts( monitor, repo, lastFix.Version, existingContent )
                                    : false;
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
        if( !CoreBuild( monitor, context, versionInfo, buildCommit, targetVersion, runTest, rebuild ) )
        {
            return false;
        }

        // Each Build call should be "atomic": we want to avoid a "holistic", global approach of the impact as much as possible.
        // A Build initial trigger is a Repo with a list of produced [(PackageId,Version)] with the same version:
        // version can be factorized.
        //
        // Not factorizing the Version at this level would let the door opened to a "Per Project Version" feature:
        // the possibility to define (and lock) the version at the project level.
        // But this is not easy because of transitive dependencies: we must ensure that a "locked" version only
        // consumes locked version or we must be able to automatically increment the "locked" version. Nightmare.
        // Moreover, this breaks the UX of the code base discovery in a repo from the version tag. Nightmare again.
        // We give up on this and keep the single versioned commit pattern that applies to all the produced packages:
        // the output of a Build is (Repo,Commit,Version,[PackageId]).
        //


        // TODO: Propagate this build where it must be propagated...
        // We first need to obtain the set of all the Release in the World that consume
        // at least one of our produced packages.
        //   [ Then we must topologically sort them to have the entry points of the graph.
        //     Then we can start building them. Parallelism would be great here... or not because
        //     too much build/test/package at the same time can put the machine on its knees. This has to be
        //     tested and a max degree of parallelism (MaxDOP) should certainly be introduced.
        //   ]
        //
        // What we need here is a list of (Repo,Commit) where:
        // - the Commit has a Release version tag.
        // - the Commit code base consumed a previous version of any of our produced packages.
        // The NuGetReleaseInfo has no knowledge of the "repository" level. It has only package/version
        // instances that may be in this World or not and this is a good thing:
        // - if a Repo appears that produces a package that was used by one Repo in the World, this "previously external"
        //   package is handled transparently.
        // - but if a project has been moved from a Repo to another one (okay, this is not exactly the cas of a Fix, but let's
        //   consider this case here), how should we handle this?
        //   First, the version number of the moved package is given by the target Repo and it must necessarily be greater
        //   than the source Repo version number. Moving a project leads to version gaps in the World's Repo.
        //   High major numbers will be the rule. This is the price to pay for the Repo/Version model.
        //   (Moving a project should be handled by CKli.)
        //   
        // We need a World database that tracks each package/version produced. Package appearance is not an issue but
        // package removal is. One could also add package rename.
        // This database has conceptually 2 relations:
        //  - Consumed( CKliRepoId, PackageId, Version )
        //  - Produced( CKliRepoId, PackageId, Version )
        // This is all the data we have. No more, no less and this should be enough to support all the required features.
        // This database is filled by the Build and can be rebuilt from a Repo's TagCommits.
        //
        //
        // The impact of the produced list is the union of the impact of each produced (PackageId,Version) so we can reason
        // on a single produced (PackageId,Version). Let's take (P1,v1.2.3).
        // 
        // The first level of Repos that are impacted are Repos in the Consumed( CKliRepoId, PackageId, Version )
        // where PackageId appears (but not the PackageId/Version to support the "Per Project Version").
        // => (*,P1,*)
        // Among all these tuples not all of them are impacted... But this depends on the workflow.
        // Here we are dealing with fixes: what are the Repo that must be fixed?
        // A fix is a stable. The consumed Version must be stable: it is not our scope to impact the prerelease versions,
        // this will be the Build of the impacted Repo that will have to handle this.
        // => (*,P1,[Stable])
        // Among the stable versions, only the ones with the same Major.Minor as the produced version must be considered.
        // => (*,P1,v1.2.*)
        // Among the stable Major.Minor versions, the one with the greatest patch is concerned.
        // But is it?
        // => (R1,P1,v1.2.1), (R2,P1,v1.2.2)
        // For R2, its fine (immediate previous fix). But what about R1 where we have the v1.2.1.
        // Where is the v1.2.2?
        // Did we miss an upgrade, or the Repo R1 doesn't consume P1 anymore?
        // To stay on the safe side, we should consider R1 as a candidate.
        // => To be robust, a "Package Upgrade" must accept unused packages and should be able to say "no change"
        // so we can skip the build.
        // 

        return true;
    }

    bool CoreBuild( IActivityMonitor monitor,
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
            return false;
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
            return false;
        }
        Branch currentHead = git.Repository.Head;
        bool mustCheckOut = currentHead.Tip.Tree.Sha != buildCommit.Tree.Sha;
        if( mustCheckOut )
        {
            monitor.Trace( $"Current working folder content is not the same as the commit '{buildCommit.Sha}' to build. Checking out (detached head)." );
            Commands.Checkout( git.Repository, buildCommit );
        }
        bool result = DoCoreBuild( monitor, context, _repoBuilder, versionInfo, buildInfo, runTest );
        if( mustCheckOut )
        {
            try
            {
                monitor.Trace( "Restoring working folder to its previous head." );
                Commands.Checkout( git.Repository, currentHead, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force } );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Error while restoring '{git.DisplayPath}' back to '{currentHead}'.", ex );
                result = false;
            }
        }
        return result;

        static bool DoCoreBuild( IActivityMonitor monitor,
                                 CKliEnv context,
                                 RepositoryBuilderPlugin repoBuilder,
                                 VersionTagInfo versionInfo,
                                 CommitBuildInfo buildInfo,
                                 bool? runTest )
        {
            var buildResult = repoBuilder.Get( monitor, versionInfo.Repo ).Build( monitor, buildInfo, runTest );
            if( !buildResult.Success )
            {
                return false;
            }
            if( !buildInfo.ApplyReleaseBuildTag( monitor, context, buildResult.Content.ToString() ) )
            {
                return false;
            }
            return true;
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
