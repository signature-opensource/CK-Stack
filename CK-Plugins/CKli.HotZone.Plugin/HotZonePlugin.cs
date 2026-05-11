using CK.Core;
using CK.PerfectEvent;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace CKli.HotZone.Plugin;

public sealed partial class HotZonePlugin : PrimaryPluginBase
{
    readonly PerfectEventSender<FixWorkflowStartEventArgs> _onFixStart;
    readonly BranchModelPlugin _branchModel;
    readonly VersionTagPlugin _versionTag;
    readonly ShallowSolutionPlugin _shallowSolution;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;

    public HotZonePlugin( PrimaryPluginContext primaryContext,
                          BranchModelPlugin branchModel,
                          VersionTagPlugin versionTag,
                          ShallowSolutionPlugin shallowSolution,
                          ReleaseDatabasePlugin releaseDatabase,
                          ArtifactHandlerPlugin artifactHandler )
        : base( primaryContext )
    {
        _onFixStart = new PerfectEventSender<FixWorkflowStartEventArgs>();
        _branchModel = branchModel;
        _versionTag = versionTag;
        _shallowSolution = shallowSolution;
        _releaseDatabase = releaseDatabase;
        _artifactHandler = artifactHandler;
    }

    /// <summary>
    /// Gets the <see cref="HotGraph"/> for a <see cref="BranchName"/>.
    /// <para>
    /// This requires that <see cref="BranchModelInfo.HasIssue"/> is false. This can handle
    /// dirty working folders: if the specified branch is currently checked out in a repository,
    /// the working folder will be analyzed.
    /// </para>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="branchName">
    /// The branch name.
    /// At least one of the <paramref name="pivots"/> must have a corresponding active <see cref="HotBranch"/>.
    /// </param>
    /// <param name="isCIBuild">True to obtain the graph from the CI point of view.</param>
    /// <param name="pivots">
    /// Optional pivots of the graph.
    /// <list type="bullet">
    ///  <item>When empty, this is the same as the <see cref="World.GetAllDefinedRepo(IActivityMonitor)"/> list.</item>
    ///  <item>When not empty, it MUST be in strict increasing <see cref="Repo.Index"/> order.</item>
    /// </list>
    /// </param>
    /// <returns>The graph or null on error.</returns>
    public HotGraph? GetHotGraph( IActivityMonitor monitor, BranchName branchName, bool isCIBuild, IReadOnlyList<Repo> pivots )
    {
        // Normalize Pivots/AllRepos.
        var allRepos = World.GetAllDefinedRepo( monitor );
        if( allRepos == null ) return null;
        bool hasPivots = pivots.Count != 0 && pivots.Count != World.Layout.Count;
        // Avoid checking the order if the provided pivots are all the repositories.
        if( hasPivots )
        {
            for( int i = 1; i < pivots.Count; i++ )
            {
                if( pivots[i - 1].Index >= pivots[i].Index )
                {
                    throw new ArgumentException( "Pivots must be in strict increasing index order." );
                }
            }
        }
        else
        {
            // No pivot => all repositories are pivots.
            pivots = allRepos;
        }
        var displayPivots = hasPivots
                                ? $"'{pivots.Select( r => r.DisplayPath.Path ).Concatenate( "', '" )}'"
                                : "all repositories";
        using( monitor.OpenTrace( $"Computing Hot Graph from '{branchName}' for {displayPivots}." ) )
        {
            if( !_branchModel.TryGetAllWithoutIssue( monitor, out _ ) )
            {
                return null;
            }
            using( monitor.OpenInfo( pivots.Count == allRepos.Count
                                        ? $"Considering branch '{branchName}' from all {allRepos.Count} repositories."
                                        : $"Considering branch '{branchName}' from {pivots.Count} pivots out of {allRepos.Count} repositories." ) )
            {
                // We can now instantiate the graph object and add all the nodes that are the HotGraph.Solution
                // instances.
                var externalPackages = _versionTag.GetPackagesConfiguration( monitor );
                if( externalPackages == null ) return null;

                var graph = new HotGraph( branchName, allRepos, pivots, _versionTag, _shallowSolution, externalPackages );
                Throw.DebugAssert( graph.HasPivots == hasPivots );
                // We must start from the pivots, from their "dev/" branch if it exists. From the solution in the "dev/" branch,
                // we read their produced projects and fill the graph Dictionary<string, Solution> ProducedPackages.
                //
                // Once the pivots have been read from their "dev/", then for the others (the non-pivots solutions):
                // - Downstream repositories must always be read from their "dev/" branch: we want the pivots to impact the
                //   "current version" of the downstream code.
                // - For upstream repositories, it depends on pullBuild:
                //   - pullBuild is false (build/publish) => upstream must be read from the regular branch.
                //   - pullBuild is true (*build/*publish) => upstream must be read from the "dev/" branch and their
                //     downstream repositories also read from the "dev/" branch. Repositories that are eventually not
                //     downstream nor upstreams must be read from their regular branch.
                //
                // The problem here is that whether a non-pivot repository is a downstream, an upstream, or nothing depends
                // on... the branch.
                //
                // The graph obtained from "regular" branches and the one from the "dev/" branches are structurally
                // different, they may (accidentally) be similar: there are 2 graphs.
                //
                foreach( var repo in allRepos )
                {
                    // Consider the closest Git branch that exists in the repository (at the BranchName level).
                    var branchInfo = _branchModel.Get( monitor, repo );
                    var hotBranch = branchInfo.GetClosestActiveBranch( branchName );
                    Throw.DebugAssert( "There is no Branch Model issue: the closest hot branch necessarily exists.", hotBranch?.GitBranch != null );

                    // Invariant (arbitrary choice): solution.IsPivot => graph.HasPivot (ie. !graph.HasPivot => !solution.IsPivot).
                    // It means that when !graph.HasPivots, then every solution is contained in the Pivots.
                    bool isContainedInPivots = !hasPivots || pivots.Contains( repo );

                    // Should we initially consider the "dev/"?
                    // - If the repo has not the theoretical graph branch, we don't want to consider the base branch's "dev/":
                    //   when building "-alpha" and the repo is only in "stable": a "-alpha" will be built (and the "alpha"
                    //   branch created) if and only if a built upstream impacts it (this is the CanBeDevSolution).
                    // - When "ci building", we always consider the "dev/" solution when possible.
                    //   But for non-ci builds, we initially consider the "/dev" only for the repos that are in pivots.
                    bool isDevSolution = (hotBranch.BranchName == branchName) && (isCIBuild || isContainedInPivots);

                    // isDevSolution may transition from false to true if the solution failed to be read in the
                    // regular branch but is valid in the "dev/".
                    if( !graph.AddSolution( monitor, repo, hotBranch, hasPivots && isContainedInPivots, ref isDevSolution ) )
                    {
                        return null;
                    }
                }
                if( graph.DevSolutions.Count == 0 )
                {
                    monitor.Error( $"Unable to find at least one repository with branch '{branchName.Name}' among {pivots.Count} repositories." );
                    return null;
                }
                // The solutions have been successfully added. The mappings from "package name" (that are the project names)
                // to the solutions are non ambiguous. We can start the topological sort.
                // The sort starts with the pivots (this will walk all the dependencies and sets the IsPivotUpstream).
                return graph.TopologicalSort( monitor )
                        ? graph
                        : null;
            }
        }
    }

    /// <summary>
    /// Basic preconditions are strict:
    /// <list type="bullet">
    ///     <item>No repo must be dirty.</item>
    ///     <item>No repo's VersionTagInfo must have issues.</item>
    ///     <item>No repo's BranchModelInfo must have issues.</item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="plannedAction">The planned action description. Example: "building" or "starting a fix".</param>
    /// <param name="allRepos">Outputs all the <see cref="Repo"/>.</param>
    /// <returns>True on success, false on error.</returns>
    public bool CheckBasicPreconditions( IActivityMonitor monitor,
                                         string plannedAction,
                                         [NotNullWhen( true )] out IReadOnlyList<Repo>? allRepos )
    {
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
                    var tags = _versionTag.Get( monitor, repo );
                    success &= !tags.HasIssue;
                    var branch = _branchModel.Get( monitor, repo );
                    success &= !branch.HasIssue;
                }
            }
            if( !success )
            {
                monitor.Error( $"Please fix any issue before {plannedAction}." );
            }
            return success;
        }
    }


}
