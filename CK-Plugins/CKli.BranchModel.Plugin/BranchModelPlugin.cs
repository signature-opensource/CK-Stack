using CK.Core;
using CK.PerfectEvent;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin : PrimaryRepoPlugin<BranchModelInfo>
{
    readonly BranchNamespace _namespace;
    readonly VersionTagPlugin _versionTags;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;
    internal readonly ShallowSolutionPlugin _shallowSolution;
    readonly PerfectEventSender<FixWorkflowStartEventArgs> _onFixStart;

    /// <summary>
    /// This is a primary plugin.
    /// </summary>
    public BranchModelPlugin( PrimaryPluginContext primaryContext,
                              VersionTagPlugin versionTags,
                              ReleaseDatabasePlugin releaseDatabase,
                              ArtifactHandlerPlugin artifactHandler,
                              ShallowSolutionPlugin shallowSolution )
        : base( primaryContext )
    {
        _namespace = new BranchNamespace( World.Name.LTSName,
                                          primaryContext.Configuration.XElement.Attribute( XNames.Branches )?.Value );
        World.Events.Issue += IssueRequested;
        _versionTags = versionTags;
        _releaseDatabase = releaseDatabase;
        _artifactHandler = artifactHandler;
        _shallowSolution = shallowSolution;
        _onFixStart = new PerfectEventSender<FixWorkflowStartEventArgs>();
    }

    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        bool hasSevereIssue = false;
        foreach( var r in e.Repos )
        {
            var info = Get( monitor, r );
            if( info.HasIssue )
            {
                info.CollectIssues( monitor, e.ScreenType, e.Add, out hasSevereIssue );
            }
        }
        if( !hasSevereIssue && ContentIssue != null )
        {
            using( monitor.OpenInfo( "Raising ContentIssue event." ) )
            {
                foreach( var r in e.Repos )
                {
                    var info = Get( monitor, r );
                    var issueBuilder = new ContentIssueBuilder( info, RaiseContentIssue );
                    if( !issueBuilder.CreateIssue( monitor, e.ScreenType, e.Add ) )
                    {
                        monitor.CloseGroup( $"ContentIssue event handling failed." );
                        // Stop on the first error.
                        break;
                    }
                }
            }
        }
    }

    bool RaiseContentIssue( IActivityMonitor monitor, ContentIssueEvent e )
    {
        Throw.DebugAssert( ContentIssue != null );
        bool success = true;
        using( monitor.OnError( () => success = false ) )
        {
            ContentIssue( e );
        }
        return success;
    }

    /// <summary>
    /// Gets the branch model.
    /// </summary>
    public BranchNamespace BranchNamespace => _namespace;

    /// <summary>
    /// Raised when repository content issues must be detected in the hot zone.
    /// </summary>
    public event Action<ContentIssueEvent>? ContentIssue;

    /// <summary>
    /// Finds the <paramref name="branchName"/> in the <see cref="BranchNamespace"/> or emits an error
    /// if this is not a valid name.
    /// </summary>
    /// <param name="monitor">The monitor to emit the error.</param>
    /// <param name="branchName">The branch name to lookup.</param>
    /// <returns>The name or null on error.</returns>
    public BranchName? GetValidBranchName( IActivityMonitor monitor, string branchName )
    {
        // If we are not on a known branch (defined by the Branch Model), give up.
        if( !_namespace.ByName.TryGetValue( branchName, out var exists ) )
        {
            monitor.Error( $"""
                Invalid branch '{branchName}'.
                Supported branches are '{_namespace.Branches.Select( b => b.Name ).Concatenate( "', '" )}'.
                """ );
        }
        return exists;
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
                    var tags = _versionTags.Get( monitor, repo );
                    success &= !tags.HasIssue;
                    var branch = Get( monitor, repo );
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
    /// <param name="ciBuild">True to obtain the graph from the CI point of view.</param>
    /// <param name="pivots">
    /// Optional pivots of the graph.
    /// <list type="bullet">
    ///  <item>When empty, this is the same as the <see cref="World.GetAllDefinedRepo(IActivityMonitor)"/> list.</item>
    ///  <item>When not empty, it MUST be in strict increasing <see cref="Repo.Index"/> order.</item>
    /// </list>
    /// </param>
    /// <returns>The graph or null on error.</returns>
    public HotGraph? GetHotGraph( IActivityMonitor monitor, BranchName branchName, bool ciBuild, IReadOnlyList<Repo> pivots )
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
            if( !TryGetAllWithoutIssue( monitor, out _ )  )
            {
                return null;
            }
            using( monitor.OpenInfo( pivots.Count == allRepos.Count
                                        ? $"Considering branch '{branchName}' from all {allRepos.Count} repositories."
                                        : $"Considering branch '{branchName}' from {pivots.Count} pivots out of {allRepos.Count} repositories." ) )
            {
                // We can now instantiate the graph object and add all the nodes that are the HotGraph.Solution
                // instances.
                var externalPackages = _versionTags.GetPackagesConfiguration( monitor );
                if( externalPackages == null ) return null;

                var graph = new HotGraph( branchName, allRepos, pivots, _versionTags, _shallowSolution, externalPackages );
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
                    var branchInfo = Get( monitor, repo );
                    var hotBranch= branchInfo.GetClosestActiveBranch( branchName );
                    Throw.DebugAssert( "There is no Branch Model issue: the closest hot branch necessarily exists.", hotBranch?.GitBranch != null );

                    // Invariant (arbitrary choice): solution.IsPivot => graph.HasPivot (ie. !graph.HasPivot => !solution.IsPivot).
                    // It means that when !graph.HasPivots, then every solution is contained in the Pivots.
                    bool isContainedInPivots = !hasPivots || pivots.Contains( repo );

                    // Should we initially consider the "dev/"?
                    // If the repo has not the theoretical graph branch, we don't want to consider the base branch's "dev/":
                    // when building "-alpha" and the repo is only in "stable": a "-alpha" will be built (and the "alpha"
                    // branch created) if and only if a built upstream impacts it.
                    bool canBeDevSolution = hotBranch.BranchName == branchName;

                    bool isDevSolution = canBeDevSolution;
                    // - But always considering "/dev" if it exists is right for the "ciBuild" mode. For non-ci builds, we
                    //   initially consider the "/dev" only for the repos that are in pivots.
                    if( !ciBuild ) canBeDevSolution &= isContainedInPivots;

                    if( !graph.AddSolution( monitor, repo, hotBranch, hasPivots && isContainedInPivots, canBeDevSolution ) )
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

    protected override BranchModelInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var info = new BranchModelInfo( repo, _namespace, this );
        var git = repo.GitRepository.Repository;
        var root = HotBranch.Create( monitor, info, repo.GitRepository, _namespace.Root );
        if( root.GitBranch == null )
        {
            if( PrimaryPluginContext.Command is not CKliIssue )
            {
                monitor.Warn( $"Missing '{root.BranchName}' branch in '{repo.DisplayPath}'. Use 'ckli issue' for details." );
            }
            // The worst issue: no root "stable" branch. This has to be resolved before doing anything else.
            info.Initialize( [root], hasIssue: true );
            return info;
        }
        // We have our hot root "stable" branch.
        bool hasIssue = root.HasIssue;
        var hotBranches = new HotBranch[_namespace.Branches.Length];
        hotBranches[0] = root;
        for( int i = 1; i < hotBranches.Length; ++i )
        {
            var branchName = _namespace.Branches[i];
            var b = HotBranch.Create( monitor, info, repo.GitRepository, branchName );
            hasIssue |= b.HasIssue;
            hotBranches[i] = b;
        }
        info.Initialize( ImmutableCollectionsMarshal.AsImmutableArray( hotBranches ), hasIssue );
        return info;
    }

    /// <summary>
    /// Tries to parse "fix/v<paramref name="major"/>.<paramref name="minor"/>".
    /// </summary>
    /// <param name="s">The name to parse.</param>
    /// <param name="major">The major version to fix.</param>
    /// <param name="minor">The minor version to fix.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParseBranchFixName( ReadOnlySpan<char> s, out int major, out int minor )
    {
        major = 0;
        minor = 0;
        return s.TryMatch( "fix/" )
               && s.TryMatch( 'v' )
               && s.TryMatchInteger( out major )
               && major >= 0
               && s.TryMatch( '.' )
               && s.TryMatchInteger( out minor )
               && minor >= 0
               && s.Length == 0;
    }
}

