using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Immutable;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo : RepoInfo
{
    readonly BranchNamespace _namespace;
    readonly BranchModelPlugin _plugin;

    // Deferred initialization.
    ImmutableArray<HotBranch> _branches;
    bool _hasIssue;

    internal BranchModelInfo( Repo repo, BranchNamespace ns, BranchModelPlugin plugin )
        : base( repo )
    {
        _namespace = ns;
        _plugin = plugin;
    }

    internal void Initialize( ImmutableArray<HotBranch> branches, bool hasIssue )
    {
        _branches = branches;
        _hasIssue = hasIssue;
    }

    /// <summary>
    /// Gets the branch namespace.
    /// </summary>
    public BranchNamespace Namespace => _namespace;

    /// <summary>
    /// Gets all the <see cref="HotBranch"/> indexed by their <see cref="BranchName.Index"/>.
    /// Their git <see cref="HotBranch.GitBranch"/> may be null.
    /// </summary>
    public ImmutableArray<HotBranch> Branches => _branches;

    /// <summary>
    /// Gets the root branch.
    /// Its <see cref="HotBranch.GitBranch"/> can be null (this has to be fixed, <see cref="HasIssue"/> is true).  
    /// </summary>
    public HotBranch Root => _branches[0];

    /// <summary>
    /// Gets the closest <see cref="HotBranch"/> with a non null <see cref="HotBranch.GitBranch"/> in the <see cref="Namespace"/>.
    /// <para>
    /// This is null only if the root "stable" branch is missing.
    /// </para>
    /// </summary>
    /// <param name="name">The starting branch.</param>
    /// <returns>The branch to consider.</returns>
    public HotBranch? GetClosestActiveBranch( BranchName name )
    {
        var b = _branches[name.Index];
        do
        {
            if( b.GitBranch != null ) return b;
            b = b.Previous;
        }
        while( b != null );
        return null;
    }

    internal ShallowSolutionPlugin ShallowSolutionPlugin => _plugin._shallowSolution;

    /// <inheritdoc />
    public override bool HasIssue => _hasIssue;

    internal void CollectIssues( IActivityMonitor monitor,
                                 ScreenType screenType,
                                 Action<World.Issue> collector,
                                 out bool hasSevereIssues )
    {
        Throw.DebugAssert( _hasIssue );
        // If the "stable" branch doesn't exist, no need to continue.
        if( Root.GitBranch == null )
        {
            // Use "dev/stable" if it exists.
            Branch? mainOrMaster = Root.GitDevBranch
                                    ?? Repo.GitRepository.GetBranch( monitor, "main", LogLevel.Info )
                                    ?? Repo.GitRepository.GetBranch( monitor, "master", LogLevel.Info );
            collector( MissingRootBranchIssue.Create( monitor, Root, mainOrMaster, screenType ) );
            hasSevereIssues = true;
            return;
        }
        var issues = new IssueBuilder();
        foreach( var b in _branches )
        {
            b.Collect( issues );
        }
        issues.CollectIssues( monitor, Repo, screenType, collector, out hasSevereIssues );
    }

}
