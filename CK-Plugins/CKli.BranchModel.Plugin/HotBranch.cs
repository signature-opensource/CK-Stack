using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

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
    readonly BranchModelInfo _info;
    BranchLink? _link;
    Branch? _gitDevBranch;

    HotBranch( BranchName name, BranchModelInfo info, Branch? gitBranch, Branch? gitDevBranch )
    {
        _name = name;
        _info = info;
        _gitDevBranch = gitDevBranch;
        if( gitBranch != null )
        {
            _link = gitDevBranch == null
                    ? BranchLink.Create( gitBranch, name.DevName )
                    : BranchLink.Create( gitBranch, gitDevBranch );
        }
    }

    internal static HotBranch Create( IActivityMonitor monitor, BranchModelInfo info, GitRepository repo, BranchName name )
    {
        var gitBranch = repo.GetBranch( monitor, name.Name, missingLocalAndRemote: CK.Core.LogLevel.None );
        var gitDevBranch = repo.GetBranch( monitor, name.DevName, missingLocalAndRemote: CK.Core.LogLevel.None );
        return new HotBranch( name, info, gitBranch, gitDevBranch );
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _info.Repo;

    /// <summary>
    /// Gets the branch information.
    /// </summary>
    public BranchModelInfo BranchModelInfo => _info;

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
    [MemberNotNullWhen( true, nameof( GitBranch ), nameof( _link ) )]
    public bool IsActive => _link != null;

    /// <summary>
    /// Gets whether <see cref="GitDevBranch"/> exists but this branch is not active.
    /// <para>
    /// The <see cref="GitDevBranch"/> branch should be deleted.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitDevBranch ) )]
    public bool HasOrphanDevBranch => _link == null && _gitDevBranch != null;

    /// <summary>
    /// Gets whether this branch has an issue that should be collected and fixed.
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
            // exist) to enlist the "orphan dev/" issue: the existing "dev/stable" is used
            // as the starting point (instead of "main" or "master").
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
    /// Commits into this <see cref="BranchLink"/>. Development always takes place is the "dev/" branch,
    /// only <see cref="IntegrateDevBranch(IActivityMonitor)"/> can commit in the <see cref="GitBranch"/>.
    /// <list type="bullet">
    ///     <item>The <see cref="GitDevBranch"/> must exists and be checked out.</item>
    ///     <item>If there's nothing to commit, nothing is done (<paramref name="message"/> is ignored).</item>
    ///     <item>On success, GitDevBranch is updated.</item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Commit( IActivityMonitor monitor, string message )
    {
        Throw.CheckState( IsActive && GitDevBranch != null && GitDevBranch.IsCurrentRepositoryHead );
        var newLink = _link.CommitAhead( monitor, Repo, message );
        if( newLink == null ) return false;
        _link = newLink;
        _gitDevBranch = _link.Ahead;
        return true;
    }

    /// <summary>
    /// Integrates <see cref="GitDevBranch"/> (that must not be null) into <see cref="GitBranch"/> and deletes it.
    /// On success, GitBranch is updated and GitDevBranch becomes null.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool IntegrateDevBranch( IActivityMonitor monitor )
    {
        Throw.CheckState( IsActive && GitDevBranch != null );
        var newLink = _link.IntegrateAhead( monitor, Repo );
        if( newLink == null ) return false;
        _link = newLink;
        Throw.DebugAssert( _link.Ahead == null );
        _gitDevBranch = null;
        return true;
    }

    /// <summary>
    /// Ensures that <see cref="GitDevBranch"/> exists.
    /// </summary>
    /// <returns>The "dev/" branch.</returns>
    [MemberNotNullWhen( true, nameof( GitDevBranch ) )]
    public Branch EnsureDevBranch()
    {
        Throw.CheckState( IsActive );
        if( _gitDevBranch == null )
        {
            _link = _link.CreateAhead( Repo );
            _gitDevBranch = _link.Ahead;
        }
        Throw.DebugAssert( _gitDevBranch != null );
        return _gitDevBranch;
    }

    /// <summary>
    /// Returns this branch name.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name.ToString();
}
