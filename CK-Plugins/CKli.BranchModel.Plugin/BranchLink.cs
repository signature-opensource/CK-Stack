using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Linq;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// A link associates a <see cref="Branch"/> to an optional <see cref="Ahead"/> branch.
/// Ahead must be based on the Branch and have commits ahead or not exist at all or
/// the <see cref="Issue"/> should be fixed.
/// <para>
/// This object is immutable.
/// </para>
/// </summary>
sealed partial class BranchLink
{
    readonly Branch _branch;
    readonly Branch? _ahead;
    readonly string _aheadName;
    readonly int _aheadBy;
    readonly int _behindBy;

    /// <summary>
    /// Gets the branch that is the base of <see cref="Ahead"/>.
    /// </summary>
    public Branch Branch => _branch;

    /// <summary>
    /// Gets the optional ahead branch.
    /// </summary>
    public Branch? Ahead => _ahead;

    /// <summary>
    /// Gets the issue that needs to be fixed regarding <see cref="Ahead"/>.
    /// </summary>
    public IssueKind Issue
    {
        get
        {
            if( _ahead == null ) return IssueKind.None;
            // If the ahead branch is not ahead, it is useless.
            // This applies even if ahead is behind the branch: it is useless
            // to resynchronize it.
            if( _aheadBy == 0 ) return IssueKind.Useless;
            if( _behindBy != 0 )
            {
                if( _behindBy == -1 )
                {
                    Throw.DebugAssert( _aheadBy == -1 );
                    return IssueKind.Unrelated;
                }
                // Ahead needs to be resynchronized.
                // 1 - We save here the edge case of the exact same content:
                //     when ahead (that is actually behind) has the same content as the branch,
                //     then we consider that it is useless.
                if( _branch.Tip.Tree.Sha == _ahead.Tip.Tree.Sha )
                {
                    return IssueKind.Useless;
                }
                // 2 - We also save the "ahead empty commit".
                if( _aheadBy == 1 && IsEmptyCommit( _ahead.Tip ) )
                {
                    return IssueKind.Useless;
                }
                return IssueKind.Desynchronized;
            }
            Throw.DebugAssert( _aheadBy > 0 );
            return IssueKind.None;

            static bool IsEmptyCommit( Commit c )
            {
                var parents = c.Parents.ToList();
                return parents.Count == 1 && c.Tree.Sha == parents[0].Tree.Sha;
            }
        }
    }

    /// <summary>
    /// Collects this link's issue if any.
    /// </summary>
    /// <param name="issues">The collector for issues.</param>
    internal void Collect( IssueBuilder issues )
    {
        if( _ahead != null )
        {
            switch( Issue )
            {
                case IssueKind.Useless:
                    issues.OnUselessBranch( _ahead, _branch );
                    break;
                case IssueKind.Unrelated:
                    issues.OnUnrelated( _ahead, _branch );
                    break;
                case IssueKind.Desynchronized:
                    issues.OnDesynchronized( _ahead, _branch, _behindBy );
                    break;
            }
        }
    }

    /// <summary>
    /// Creates the <see cref="Ahead"/> branch on an empty commit.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="committer">The Git committer to use.</param>
    /// <returns>An updated link (replaces this one).</returns>
    internal BranchLink CreateAhead( Repo repo, Signature committer )
    {
        Throw.DebugAssert( _ahead == null );
        Throw.DebugAssert( repo.GitRepository.Repository == ((IBelongToARepository)_branch).Repository );
        var git = repo.GitRepository.Repository;
        var baseCommit = _branch.Tip;
        var c = git.ObjectDatabase.CreateCommit( committer,
                                                 committer,
                                                 $"""
                                                 Initializing '{_aheadName}'.

                                                 """,
                                                 baseCommit.Tree,
                                                 [baseCommit],
                                                 prettifyMessage: false );
        var ahead = git.Branches.Add( _aheadName, c );
        return new BranchLink( _branch, ahead, _aheadName, 1, 0 );
    }

    BranchLink( Branch b, Branch? ahead, string aheadName, int aheadBy, int behindBy )
    {
        _branch = b;
        _ahead = ahead;
        _aheadName = aheadName;
        _aheadBy = aheadBy;
        _behindBy = behindBy;
    }

    /// <summary>
    /// Factory method for a link with an existing <see cref="Ahead"/> branch.
    /// </summary>
    /// <param name="b">The base branch.</param>
    /// <param name="ahead">The branch ahead.</param>
    /// <returns>The link.</returns>
    internal static BranchLink Create( Branch b, Branch ahead )
    {
        Throw.DebugAssert( b != null && ahead != null );
        var git = ((IBelongToARepository)b).Repository;
        int aheadBy;
        int behindBy;
        var d = git.ObjectDatabase.CalculateHistoryDivergence( ahead.Tip, b.Tip );
        Throw.DebugAssert( (d.CommonAncestor == null) == (d.AheadBy is null && d.BehindBy is null) );
        if( d.CommonAncestor == null )
        {
            Throw.DebugAssert( d.AheadBy is null && d.BehindBy is null );
            // => Unrelated
            aheadBy = behindBy = -1;
        }
        else
        {
            Throw.DebugAssert( d.AheadBy is not null && d.BehindBy is not null );
            aheadBy = d.AheadBy.Value;
            behindBy = d.BehindBy.Value;
        }
        return new BranchLink( b, ahead, ahead.FriendlyName, aheadBy, behindBy );
    }

    /// <summary>
    /// Factory method for a link with a <see cref="Ahead"/> branch that may exist or not.
    /// </summary>
    /// <param name="b">The base branch.</param>
    /// <param name="aheadName">The name of the ahead branch.</param>
    /// <returns>The link.</returns>
    internal static BranchLink Create( Branch b, string aheadName )
    {
        Throw.DebugAssert( b != null && !string.IsNullOrWhiteSpace( aheadName ) );
        var git = ((IBelongToARepository)b).Repository;
        var ahead = git.Branches[aheadName];
        return ahead != null
                ? Create( b, ahead )
                : new BranchLink( b, null, aheadName, 0, 0 );
    }
}
