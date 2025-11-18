using LibGit2Sharp;

namespace CKli.BranchModel.Plugin;

public sealed class BranchInfo
{
    readonly BranchNode _expected;
    readonly BranchInfo? _theoreticalBaseBranch;
    readonly BranchInfo? _existingBaseBranch;
    readonly Branch? _branch;
    readonly HistoryDivergence? _divergence;

    internal BranchInfo( BranchNode expected,
                         BranchInfo? theoreticalBaseBranch,
                         BranchInfo? existingBaseBranch,
                         Branch? branch,
                         HistoryDivergence? divergence )
    {
        _expected = expected;
        _theoreticalBaseBranch = theoreticalBaseBranch;
        _existingBaseBranch = existingBaseBranch;
        _branch = branch;
        _divergence = divergence;
    }

    /// <summary>
    /// Gets the expected branch from the branch model.
    /// </summary>
    public BranchNode Expected => _expected;

    /// <summary>
    /// Gets the base branch if this branch is not the root "stable" one.
    /// <para>
    /// This base branch reflects the <see cref="BranchNode.Base"/>. If it doesn't exist in the
    /// repository, <see cref="ExistingBaseBranch"/> is the actual base to consider that ultimately
    /// is the root "stable" branch.
    /// </para>
    /// </summary>
    public BranchInfo? TheoreticalBaseBranch => _theoreticalBaseBranch;

    /// <summary>
    /// Gets the base branch if this branch is not the root "stable" one that exists
    /// in the repository.
    /// <para>
    /// This is null only for the root "stable" branch or if the "stable" branch itself doesn't
    /// exist in the repository.
    /// </para>
    /// </summary>
    public BranchInfo? ExistingBaseBranch => _existingBaseBranch;

    /// <summary>
    /// Gets the repository's <see cref="Branch"/> if it exists.
    /// </summary>
    public Branch? Branch => _branch;

    /// <summary>
    /// Gets the <see cref="HistoryDivergence"/> between this <see cref="Branch"/> and the base one if they both
    /// exist in the repository.
    /// </summary>
    public HistoryDivergence? Divergence => _divergence;

}
