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
using LogLevel = CK.Core.LogLevel;
using CKli.ReleaseDatabase.Plugin;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;

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
            || !CheckBuildPreconditions( monitor, out var allRepos ) )
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
            || !CheckBuildPreconditions( monitor, out var allRepos ) )
        {
            return false; 
        }
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        Throw.DebugAssert( "Preconditions fulfilled.", repo != null && !repo.GitStatus.IsDirty );
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

        if( BranchModelPlugin.TryParseBranchFixName( branchName, out var devFix, out var fixMajor, out var fixMinor ) )
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

    bool CheckBuildPreconditions( IActivityMonitor monitor, [NotNullWhen(true)]out IReadOnlyList<Repo>? allRepos )
    {
        // Build preconditions are strict:
        // - No repo must be dirty.
        // - No repo's VersionTagInfo must have issues.
        // - No repo's BranchModelInfo must have issues.
        using( monitor.OpenTrace( $"Checking World's global state." ) )
        {
            bool success = true;
            allRepos = World.GetAllDefinedRepo( monitor );
            if( allRepos == null ) return false;
            foreach( var repo in allRepos )
            {
                if( !repo.GitRepository.CheckCleanCommit( monitor ) )
                {
                    success = false;
                }
                else
                {
                    var tags = _versionTags.Get( monitor, repo );
                    success &= !tags.HasIssues;
                    var branch = _branchModel.Get( monitor, repo );
                    success &= !branch.HasIssues;
                }
            }
            if( !success )
            {
                monitor.Error( $"Please fix any issue before building." );
            }
            return success;
        }
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
            // TODO: Here we should consider isDevBranch and upgrade the dependencies to be in CI
            //       before building...
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
        if( branch != r.Head && !repo.GitRepository.SetCurrentBranch( monitor, branchName, skipPullMerge: true ) )
        {
            return false;
        }
        // If we are not rebuilding an existing tagged commit, the buildCommit is now the head's tip.
        buildCommit ??= r.Head.Tip;

        // We are ready to build a fix.
        var result = CoreBuild( monitor, context, versionInfo, buildCommit, targetVersion, runTest, rebuild );
        if( result == null )
        {
            return false;
        }
        //
        // We introduce a check here: we demand that the produced package identifiers are the same as the release
        // we are fixing: changing the produced packages that are structural/architectural artifacts is
        // everything but fixing.
        // We do this before checking if there are produced package identifiers.
        //
        if( !result.Content.Produced.SequenceEqual( lastFix.BuildContentInfo.Produced ) )
        {
            monitor.Error( $"""
                Forbidden change in produced packages for a fix in '{repo.DisplayPath}':
                The version '{lastFix.Version.ParsedText}' produced packages: '{lastFix.BuildContentInfo.Produced.Concatenate("', '")}'.
                But the new fix 'v{result.Version}' produced: '{result.Content.Produced.Concatenate( "', '" )}'.
                """ );
            _versionTags.DestroyLocalRelease( monitor, repo, result.Version );
            return false;
        }
        // If there's no production, we are done.
        if( result.Content.Produced.Length == 0 )
        {
            return true;
        }
        // The impacts are all the Repo's LastMajorMinorStables commits that have one of our content's
        // produced package identifiers consumed with the lastFix.Version.
        // A lighter impact can be to consider only the Repo's LastStable... But here we are on a "fix/"
        // branch of a somehow "past" release, we are not releasing in the "hot zone" (as a new minor stable version):
        // it seems coherent to propagate "widely in the past"... But this may produce a lot of useless releases...
        //
        // Is there a way to "opt in" or "opt out" the propagation? It must be:
        //   - easy to initiate and to stop.
        //   - easy to grasp (by looking at the repository).
        // Idea! Can it be the "fix/" branch that does the job?
        // Given a fix of "My-Core/v1.0.1", a repo consumed this package in its v1.0.0 for its own versions
        // - v4.0.0 (LastStable, LastMajorMinorStable)
        // - v3.1.2 (LastMajorMinorStable)
        // - v3.1.1 (out of this scope: not in the LastMajorMinorStable)
        // - v3.1.1 (same as above)
        // - v3.0.1 (LastMajorMinorStable)
        // - v3.0.0 (out of this scope: not in the LastMajorMinorStable)
        // - v2.0.1 (LastMajorMinorStable)
        // - v2.0.0 (out of this scope: not in the LastMajorMinorStable)
        //
        // Using the LastMajorMinorStable, this triggers 4 new versions (4.0.1, 3.1.3, 3.0.2 and 2.0.2) which, in turn, produce
        // more versions of downstream repos.
        // Is the 2.0.2 useful? required?
        // If we really don't care of v2 anymore, then a +deprecated tag can/should be created (this solves the problem:
        // deprecated versions are no more "regular" versions and are ignored).
        // But if we consider +deprecated as a "strong signal" that shouldn't be overused, then we need another mechanism
        // that may be the existence of the "fix/" branch (here, does a "fix/v2.0" exist or not?).
        //  - Is this branch "automatically" created (opt-out mode)?
        //    Pros: safe. Cons: when will they be deleted? by who? (answer: almost never...)
        //  - Opt-in mode? It's so easy to forget to create it...
        //  => None is good.
        // Changing the point of view: a --critical-fix flag makes us use the LastMajorMinorStable otherwise
        // only the LastStable is used.
        // This is far better (especially regarding the fact that in practice CK has a "upgrade asap" philosophy).
        // But... wait... We started this discussion with
        //      "But here we are on a "fix/" branch of a somehow "past" release, we are not releasing in the "hot zone"
        //      (as a new minor stable version): it seems coherent to propagate "widely in the past"... "
        // I initially thought that the "build fix" was easier than working in the "hot zone". But this is not that obvious.
        // Is it because we are "pushing downstream" rather that thinking "pulling upstream"?
        // What does pulling means here. Something like:
        // "Hey, World, please upgrade ALL your dependencies to available patch versions and gives me updated packages.".
        // The question is eventually the same: which packages for which (potentially outdated) versions?
        // No... We definitely want to push fixes downstream.
        //
        // May be the solution is using the +deprecated: a regular version is "alive" (and must be fixed).
        // A +deprecated version is "dead" and should not be used anymore. If we deprecate aggressively, we won't have
        // these problems (and leads to less versions to manage) that globally optimize the system.
        //
        // To conclude: The impacts of a fix are all the Repo's LastMajorMinorStables commits that have one of
        //              our content's produced package identifiers consumed with the lastFix.Version.

        Throw.Assert( "Preconditions: no version tag issue.", _versionTags.TryGetAll( monitor, out var allRepoVersions ) );
        var releaseInfo = _releaseDatabase.GetReleaseInfo( monitor, repo, lastFix.Version );
        if( releaseInfo == null )
        {
            monitor.Error( ActivityMonitor.Tags.ToBeInvestigated,
                $"Release '{repo.DisplayPath}/{lastFix.Version}' cannot be found in the Release database and there's no Tag issue. This should not happen." );
            _versionTags.DestroyLocalRelease( monitor, repo, result.Version );
            return false;
        }
        IReadOnlyList<RepoReleaseInfo> firstImpacts = releaseInfo.GetDirectConsumers( monitor );
        IEnumerable<NuGetPackageInstance> initialUpgradeList = result.Content.Produced.Select( id => new NuGetPackageInstance( id, result.Version ) );

        // Build the "update list".
        var updateList = result.Content.Produced.Select( id => new NuGetPackageInstance( id, result.Version ) ).ToImmutableArray();
        foreach( var rV in allRepoVersions )
        {
            foreach( var tc in rV.LastMajorMinorStables )
            {
                Throw.DebugAssert( tc.IsRegularVersion && tc.BuildContentInfo != null );
                if( tc.BuildContentInfo.Consumed.AsSpan().ContainsAny( updateList.AsSpan() ) )
                {
                    // We must build the
                    var targetBranchName = $"{(isDevBranch ? "dev/" : "")}fix/v{tc.Version.Major}.{tc.Version.Minor}";
                }
            }
        }


        return true;
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

sealed class BuildGraph
{
    /// <summary>
    /// Implies that all the <see cref="BuildTarget.BuildBranch"/> are "dev/" branches.
    /// </summary>
    public bool IsCIBuild { get; }

    /// <summary>
    /// Ever increasing set of package instances to upgrade <see cref="BuildTarget"/>.
    /// </summary>
    public IReadOnlyDictionary<string,NuGetPackageInstance> Upgrades { get; }
}


sealed class BuildTarget
{
    public BuildGraph Graph { get; }

    public Repo Repo { get; set; }

    public string BuildBranch { get; }

    public ImmutableArray<NuGetPackageInstance> Consumed { get; }

    public ImmutableArray<string> Produced { get; }
}
