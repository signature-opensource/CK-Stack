using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo : RepoInfo
{
    readonly BranchNamespace _namespace;
    readonly ImmutableArray<HotBranch> _branches;
    readonly bool _hasIssue;

    internal BranchModelInfo( Repo repo,
                              BranchNamespace ns,
                              ImmutableArray<HotBranch> branches,
                              bool hasIssue )
        : base( repo )
    {
        _namespace = ns;
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
    /// Gets the closest existing git branch in the <see cref="Namespace"/>.
    /// <para>
    /// This is null only if the root "stable" branch is missing.
    /// </para>
    /// </summary>
    /// <param name="name">The starting branch.</param>
    /// <returns>The Git branch to consider.</returns>
    public HotGitBranch? GetClosestGitBranch( BranchName name )
    {
        var b = _branches[name.Index];
        if( b.GitDevBranch != null )
        {
            return new HotGitBranch( b, b.GitDevBranch );
        }

        do
        {
            if( b.GitBranch != null )
            {
                return new HotGitBranch( b, b.GitBranch );
            }

            b = b.Previous;
        }
        while( b != null );
        return null;
    }

    /// <inheritdoc />
    public override bool HasIssue => _hasIssue;

    internal void CollectIssues( IActivityMonitor monitor,
                                 ScreenType screenType,
                                 Action<World.Issue> collector )
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
            return;
        }
        var issues = new IssueBuilder();
        foreach( var b in _branches )
        {
            b.Collect( issues );
        }
        issues.CollectIssues( monitor, Repo, screenType, collector );
    }

}
