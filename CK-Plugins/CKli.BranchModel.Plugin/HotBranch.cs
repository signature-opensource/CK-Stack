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
    Branch? _gitBranch;
    Branch? _gitDevBranch;
    int? _devAheadBy;
    int? _devBehindBy;
    [AllowNull]
    internal BranchModelInfo _info;

    HotBranch( Repository repo, BranchName name, Branch? gitBranch, Branch? gitDevBranch )
    {
        _name = name;
        _gitBranch = gitBranch;
        _gitDevBranch = gitDevBranch;
        if( gitBranch != null && gitDevBranch != null )
        {
            var d  = repo.ObjectDatabase.CalculateHistoryDivergence( gitDevBranch.Tip, gitBranch.Tip );
            _devAheadBy = d.AheadBy;
            _devBehindBy = d.BehindBy;
        }
    }

    internal static HotBranch Create( IActivityMonitor monitor, GitRepository repo, BranchName name )
    {
        var gitBranch = repo.GetBranch( monitor, name.Name, missingLocalAndRemote: CK.Core.LogLevel.None );
        var gitDevBranch = repo.GetBranch( monitor, name.DevName, missingLocalAndRemote: CK.Core.LogLevel.None );
        return new HotBranch( repo.Repository, name, gitBranch, gitDevBranch );
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
    /// Gets the repository's branch if it exists.
    /// </summary>
    public Branch? GitBranch => _gitBranch;

    /// <summary>
    /// Gets the repository's "dev/<see cref="GitBranch"/>" branch if it exists.
    /// </summary>
    public Branch? GitDevBranch => _gitDevBranch;

    /// <summary>
    /// Gets whether the <see cref="GitBranch"/> exists in the repository.
    /// </summary>
    [MemberNotNullWhen( true, nameof(GitBranch) )]
    public bool IsActive => _gitBranch != null;

    /// <summary>
    /// Gets whether <see cref="GitDevBranch"/> exists without <see cref="GitBranch"/> in the repository.
    /// <para>
    /// The <see cref="GitDevBranch"/> branch should be deleted.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitDevBranch ) )]
    public bool HasOrphanDevBranch => _gitBranch == null && _gitDevBranch != null;

    /// <summary>
    /// Gets whether <see cref="GitDevBranch"/> and <see cref="GitBranch"/> exist but are unrelated.
    /// <para>
    /// This should barely happen and should be fixed manually.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitBranch ), nameof( GitDevBranch ) )]
    public bool HasUnrelatedDevBranch => _gitBranch != null && _gitDevBranch != null && !_devAheadBy.HasValue;

    /// <summary>
    /// Gets whether the <see cref="GitDevBranch"/> is useless as it has been integrated in the <see cref="GitBranch"/>.
    /// <para>
    /// The <see cref="GitDevBranch"/> branch should be deleted.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitBranch ), nameof( GitDevBranch ) )]
    public bool HasIntegratedDevBranch => _devAheadBy.HasValue && _devAheadBy.Value == 0;

    /// <summary>
    /// Gets whether the <see cref="GitDevBranch"/> needs to be rebased on its <see cref="GitBranch"/>.
    /// <para>
    /// Rather than a rebase (that can lead to history troubles),  should be merged into this branch.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitBranch ), nameof( GitDevBranch ) )]
    public bool HasDesynchronizedDevBranch
    {
        get
        {
            if( _devBehindBy.HasValue && _devBehindBy.Value > 0 )
            {
                Throw.DebugAssert( _gitBranch != null && _gitDevBranch != null );
                // If the "dev/" branch is integrated, it can be suppressed, not merged. 
                if( _devAheadBy.HasValue && _devAheadBy.Value == 0 )
                {
                    return false;
                }
                // Handle the edge case of an empty commit on the child branch (or a "magically" same content).
                return _gitDevBranch.Tip.Tree.Sha != _gitBranch.Tip.Tree.Sha;
            }
            return false;
        }
    }

    internal void CreateDevBranch( Signature committer )
    {
        Throw.DebugAssert( _gitBranch != null && _gitDevBranch == null );
        var git = ((IBelongToARepository)_gitBranch).Repository;

        var baseCommit = _gitBranch.Tip;
        var c = git.ObjectDatabase.CreateCommit( committer,
                                                 committer,
                                                 $"""
                                                 Initializing '{_name}'.

                                                 """,
                                                 baseCommit.Tree,
                                                 [baseCommit],
                                                 prettifyMessage: false );
        _gitDevBranch = git.Branches.Add( _name.Name, c );
        _devAheadBy = 0;
        _devBehindBy = 0;
    }

    /// <summary>
    /// Returns this branch name.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name.ToString();
}
