using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Models a branch defined by a <see cref="BranchName"/> in a <see cref="BranchModelInfo"/> for a repository.
/// <para>
/// The actual <see cref="GitBranch"/> may not exist.
/// </para>
/// </summary>
public sealed class HotBranch
{
    readonly BranchName _name;
    readonly HotBranch? _activeBase;
    readonly HistoryDivergence? _divergence;
    Branch? _gitBranch;
    [AllowNull]
    internal BranchModelInfo _info;

    internal HotBranch( Repository repo,
                        BranchName name,
                        HotBranch? existingBaseBranch,
                        Branch? branch )
    {
        _name = name;
        _activeBase = existingBaseBranch;
        _gitBranch = branch;
        _divergence = branch != null && existingBaseBranch?.GitBranch != null
                               ? repo.ObjectDatabase.CalculateHistoryDivergence( branch.Tip, existingBaseBranch.GitBranch.Tip )
                               : null;
    }

    /// <summary>
    /// Gets the <see cref="BranchModelInfo"/> that defines this branch.
    /// </summary>
    public BranchModelInfo Info => _info;

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _info.Repo;

    /// <summary>
    /// Gets the branch name from the branch namespace.
    /// </summary>
    public BranchName BranchName => _name;

    /// <summary>
    /// Gets whether this is a "dev/XXX" branch.
    /// </summary>
    public bool IsDevBranch => _name.IsDevBranch;

    /// <summary>
    /// Gets the base branch that exists in the repository (its <see cref="GitBranch"/> is not null).
    /// <para>
    /// This is null for the root "stable" branch, if the "stable" branch itself doesn't
    /// exist in the repository or if this branch is disconnected.
    /// </para>
    /// </summary>
    public HotBranch? ActiveBase => _activeBase;

    /// <summary>
    /// Gets the repository's branch if it exists.
    /// </summary>
    public Branch? GitBranch => _gitBranch;

    /// <summary>
    /// Gets the <see cref="HistoryDivergence"/> between this <see cref="GitBranch"/> and the <see cref="ActiveBase"/>
    /// if they both exist in the repository.
    /// </summary>
    public HistoryDivergence? Divergence => _divergence;

    /// <summary>
    /// Gets whether the <see cref="GitBranch"/> exists in the repository.
    /// </summary>
    public bool IsActive => _gitBranch != null;

    /// <summary>
    /// Gets whether this is an existing "dev/XXX" branch but its regular "XXX" branch doesn't exist in the repository.
    /// <para>
    /// This branch should be deleted.
    /// </para>
    /// </summary>
    public bool IsOrphanDevBranch => _name.IsDevBranch && _gitBranch != null && _activeBase?.BranchName != _name.Base;

    /// <summary>
    /// Gets whether this branch is active but doesn't bring anything to its <see cref="ActiveBase"/>:
    /// this branch can be suppressed.
    /// </summary>
    [MemberNotNullWhen( true, nameof( Divergence ), nameof( GitBranch ), nameof( ActiveBase ) )]
    public bool IsIntegratedBranch => _divergence != null && _divergence.AheadBy.HasValue && _divergence.AheadBy.Value == 0;

    /// <summary>
    /// Gets whether this branch is active but needs to be rebased on its <see cref="ActiveBase"/>.
    /// <para>
    /// Rather than a rebase (that can lead to history troubles), the base branch should be merged into this branch.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( Divergence ), nameof( GitBranch ), nameof( ActiveBase ) )]
    public bool IsDesynchronizedBranch
    {
        get
        {
            if( _divergence != null && _divergence.BehindBy.HasValue && _divergence.BehindBy.Value > 0 )
            {
                Throw.DebugAssert( _gitBranch != null && _activeBase != null && _activeBase._gitBranch != null );
                // Handle the edge case of an empty commit on the child branch (or a "magically" same content).
                return _gitBranch.Tip.Tree.Sha != _activeBase._gitBranch.Tip.Tree.Sha;
            }
            return false;
        }
    }

    internal void CreateGitBranch( Signature committer )
    {
        Throw.DebugAssert( _gitBranch == null );
        // When the "stable" branch doesn't exist, no other HotBranch are instantiated.
        Throw.CheckState( "The root \"stable\" branch cannot be created by this method.", ActiveBase != null );
        Throw.DebugAssert( _activeBase?._gitBranch != null );
        var git = ((IBelongToARepository)_activeBase!._gitBranch).Repository;

        var baseCommit = _activeBase._gitBranch.Tip;
        var c = git.ObjectDatabase.CreateCommit( committer,
                                                 committer,
                                                 $"""
                                                 Initializing '{_name}'.

                                                 """,
                                                 baseCommit.Tree,
                                                 [baseCommit],
                                                 prettifyMessage: false );
        _gitBranch = git.Branches.Add( _name.Name, c );
    }

    /// <summary>
    /// Returns this branch name.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name.ToString();
}
