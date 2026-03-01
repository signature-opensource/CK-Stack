using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Models a branch defined by a <see cref="BranchName"/> in a <see cref="BranchModelInfo"/> for a repository.
/// <para>
/// The actual <see cref="GitBranch"/> and/or <see cref="GitDevBranch"/> may not exist.
/// </para>
/// </summary>
public sealed class HotBranch
{
    readonly BranchName _name;
    BranchLink? _link;
    Branch? _gitDevBranch;
    [AllowNull]
    internal BranchModelInfo _info;

    HotBranch( BranchName name, Branch? gitBranch, Branch? gitDevBranch )
    {
        _name = name;
        _gitDevBranch = gitDevBranch;
        if( gitBranch != null )
        {
            _link = gitDevBranch == null
                    ? BranchLink.Create( gitBranch, name.DevName )
                    : BranchLink.Create( gitBranch, gitDevBranch );
        }
    }

    internal static HotBranch Create( IActivityMonitor monitor, GitRepository repo, BranchName name )
    {
        var gitBranch = repo.GetBranch( monitor, name.Name, missingLocalAndRemote: CK.Core.LogLevel.None );
        var gitDevBranch = repo.GetBranch( monitor, name.DevName, missingLocalAndRemote: CK.Core.LogLevel.None );
        return new HotBranch( name, gitBranch, gitDevBranch );
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _info.Repo;

    /// <summary>
    /// Gets the branch name from the branch namespace.
    /// </summary>
    public BranchName BranchName => _name;

    /// <summary>
    /// Gets the repository's branch if it exists.
    /// </summary>
    public Branch? GitBranch => _link?.Branch;

    /// <summary>
    /// Gets the repository's "dev/<see cref="GitBranch"/>" branch if it exists.
    /// </summary>
    public Branch? GitDevBranch => _gitDevBranch;

    /// <summary>
    /// Gets whether the <see cref="GitBranch"/> exists in the repository.
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitBranch ) )]
    public bool IsActive => _link != null;

    /// <summary>
    /// Gets whether <see cref="GitDevBranch"/> exists without <see cref="GitBranch"/> in the repository.
    /// <para>
    /// The <see cref="GitDevBranch"/> branch should be deleted.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitDevBranch ) )]
    public bool HasOrphanDevBranch => _link == null && _gitDevBranch != null;

    /// <summary>
    /// Gets whether this branch has an issue that should be collected.
    /// </summary>
    public bool HasIssue => _link != null
                                ? _link.Issue != BranchLink.IssueKind.None
                                : _name.Index == 0 || _gitDevBranch != null;

    internal void Collect( IssueBuilder issues )
    {
        if( _link == null )
        {
            // When _name.Index == 0, it is the "Missing root branch" case.
            // We don't handle it here but we avoid (if the "dev/stable" branch
            // exist) to enlist the "orphan dev/" issue.
            if( _name.Index != 0 && _gitDevBranch != null )
            {
                issues.OnMissingBaseBranch( _gitDevBranch, _name.Name );
            }
        }
        else
        {
            _link.Collect( issues );
        }
    }

    /// <summary>
    /// Gets the previous <see cref="HotBranch"/> in <see cref="BranchModelInfo.Branches"/>.
    /// Null if this is the root "stable" branch.
    /// </summary>
    public HotBranch? Previous => _name.Index > 0 ? _info.Branches[_name.Index - 1] : null;

    /// <summary>
    /// Gets the next <see cref="HotBranch"/> in <see cref="BranchModelInfo.Branches"/>.
    /// Null if this is the last, most instable, branch.
    /// </summary>
    public HotBranch? Next
    {
        get
        {
            int i = _name.Index + 1;
            return i < _info.Branches.Length ? _info.Branches[i] : null;
        }
    }

    /// <summary>
    /// Returns this branch name.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name.ToString();
}
