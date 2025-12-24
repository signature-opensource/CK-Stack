using CK.Core;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

public sealed class HotBranch
{
    readonly BranchName _name;
    readonly HotBranch? _theoreticalBaseBranch;
    readonly HotBranch? _existingBaseBranch;
    readonly Branch? _gitBranch;
    readonly HistoryDivergence? _divergence;

    internal HotBranch( BranchName name,
                        HotBranch? theoreticalBaseBranch,
                        HotBranch? existingBaseBranch,
                        Branch? branch,
                        HistoryDivergence? divergence )
    {
        _name = name;
        _theoreticalBaseBranch = theoreticalBaseBranch;
        _existingBaseBranch = existingBaseBranch;
        _gitBranch = branch;
        _divergence = divergence;
    }

    /// <summary>
    /// Gets the branch name from the branch namespace.
    /// </summary>
    public BranchName BranchName => _name;

    /// <summary>
    /// Gets the base branch if this branch is not the root "stable" one.
    /// <para>
    /// This base branch reflects the <see cref="BranchName.Base"/>. If it doesn't exist in the
    /// repository, <see cref="ExistingBaseBranch"/> is the actual base to consider that ultimately
    /// is the root "stable" branch.
    /// </para>
    /// </summary>
    public HotBranch? TheoreticalBaseBranch => _theoreticalBaseBranch;

    /// <summary>
    /// Gets whether this is a "dev/XXX" branch.
    /// </summary>
    [MemberNotNullWhen(true, nameof( TheoreticalBaseBranch ) )]
    public bool IsDevBranch => _name.IsDevBranch;

    /// <summary>
    /// Gets the base branch that exists in the repository (its <see cref="GitBranch"/> is not null).
    /// <para>
    /// This is null only for the root "stable" branch or if the "stable" branch itself doesn't
    /// exist in the repository.
    /// </para>
    /// </summary>
    public HotBranch? ExistingBaseBranch => _existingBaseBranch;

    /// <summary>
    /// Gets the repository's branch if it exists.
    /// </summary>
    public Branch? GitBranch => _gitBranch;

    /// <summary>
    /// Gets the <see cref="HistoryDivergence"/> between this <see cref="GitBranch"/> and the <see cref="ExistingBaseBranch"/>
    /// if they both exist in the repository.
    /// </summary>
    public HistoryDivergence? Divergence => _divergence;

    /// <summary>
    /// Gets whether this is an existing "dev/XXX" branch but its regular "XXX" branch doesn't exist in the repository.
    /// <para>
    /// This branch should be deleted.
    /// </para>
    /// </summary>
    public bool IsOrphanDevBranch => _name.IsDevBranch && _gitBranch != null && _existingBaseBranch?.BranchName != _name.Base;

    /// <summary>
    /// Gets whether this branch exists and doesn't bring anything to its <see cref="ExistingBaseBranch"/>.
    /// <para>
    /// This branch can be suppressed.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( Divergence ), nameof( GitBranch ), nameof( ExistingBaseBranch ) )]
    public bool IsIntegratedBranch => _divergence != null && _divergence.AheadBy.HasValue && _divergence.AheadBy.Value == 0;

    /// <summary>
    /// Gets whether this branch exists but needs to be rebased on its <see cref="ExistingBaseBranch"/>.
    /// <para>
    /// Rather than a rebase (that can lead to history troubles), the base branch should be merged into this branch.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( Divergence ), nameof( GitBranch ), nameof( ExistingBaseBranch ) )]
    public bool IsDesynchronizedBranch
    {
        get
        {
            if( _divergence != null && _divergence.BehindBy.HasValue && _divergence.BehindBy.Value > 0 )
            {
                Throw.DebugAssert( _gitBranch != null && _existingBaseBranch._gitBranch != null );
                // Handle the edge case of an empty commit on the child branch (or a "magically" same content).
                return _gitBranch.Tip.Tree.Sha != _existingBaseBranch._gitBranch.Tip.Tree.Sha;
            }
            return false;
        }
    }

    /// <summary>
    /// Returns this branch name.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name.ToString();
}
