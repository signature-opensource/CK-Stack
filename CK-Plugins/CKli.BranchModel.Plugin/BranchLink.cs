using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Linq;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// A link associates a <see cref="Branch"/> to an optional <see cref="Ahead"/> branch.
/// Ahead must be based on the Branch and have commits ahead or not exist at all or
/// the <see cref="Issue"/> should be fixed.
/// <para>
/// This object is immutable.
/// </para>
/// </summary>
public sealed partial class BranchLink
{
    static readonly MergeTreeOptions _mergeOptions = new MergeTreeOptions() { FailOnConflict = true, SkipReuc = true };

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
    /// <returns>An updated link (replaces this one).</returns>
    internal BranchLink CreateAhead( Repo repo )
    {
        Throw.CheckState( Ahead == null );
        Branch ahead = CreateAheadBranch( repo.GitRepository, _branch.Tip, _aheadName );
        return new BranchLink( _branch, ahead, _aheadName, 1, 0 );
    }

    /// <summary>
    /// Commits the <see cref="Ahead"/> branch that must be currently checked out.
    /// On success, return an updated link.
    /// <para>
    /// Note that there may have been independent commits done before this one: even if there's
    /// nothing to commit, the returned link is up to date.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>An updated link (replaces this one) on success, null on error.</returns>
    internal BranchLink? CommitAhead( IActivityMonitor monitor, Repo repo, string message )
    {
        Throw.CheckState( Ahead != null && Ahead.IsCurrentRepositoryHead );
        Throw.CheckArgument( repo.GitRepository.Repository == ((IBelongToARepository)Ahead).Repository );
        return repo.GitRepository.Commit( monitor, message ) switch
        {
            CommitResult.Error => null,
            _ => Create( _branch, repo.GitRepository.Repository.Head )
        };
    }

    /// <summary>
    /// Integrates the <see cref="Ahead"/> branch (that must be not null) into the <see cref="Branch"/> and
    /// deletes it. On success, a new <see cref="BranchLink"/> (that replaces this one) is returned.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <returns>An updated link (replaces this one) on success, null on error.</returns>
    internal BranchLink? IntegrateAhead( IActivityMonitor monitor, Repo repo )
    {
        Throw.CheckState( Ahead != null );
        var newBase = IntegrateMerge( monitor, repo.GitRepository, _ahead!, _branch );
        return newBase == null
                ? null
                : new BranchLink( newBase, null, _aheadName, 0, 0 );
    }

    /// <summary>
    /// Creates an "empty ahead commit" from a base commit for a new branch.
    /// </summary>
    /// <param name="git">The git repository.</param>
    /// <param name="baseCommit">The base commit.</param>
    /// <param name="aheadBranchName">The name of the branch to create.</param>
    /// <returns>The git branch.</returns>
    internal static Branch CreateAheadBranch( GitRepository git, Commit baseCommit, string aheadBranchName )
    {
        Throw.DebugAssert( git.Repository == ((IBelongToARepository)baseCommit).Repository );
        var c = git.Repository.ObjectDatabase.CreateCommit( baseCommit.Author,
                                                            git.Committer, $"""
                                                            Initializing '{aheadBranchName}'.

                                                            """,
                                                            baseCommit.Tree,
                                                            [baseCommit],
                                                            prettifyMessage: false );
        return git.Repository.Branches.Add( aheadBranchName, c );
    }

    /// <summary>
    /// Tries to merge the <paramref name="baseBranch"/> in the <paramref name="branch"/> (the "ahead" branch).
    /// On success, the updated ahead branch is returned.
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="git">The repository.</param>
    /// <param name="branch">The (ahead) branch.</param>
    /// <param name="baseBranch">The base branch.</param>
    /// <param name="errorLevel">Log level to use on error.</param>
    /// <returns>The updated ahead branch on success, null on error.</returns>
    public static Branch? SynchronizeMerge( IActivityMonitor monitor,
                                            GitRepository git,
                                            Branch branch,
                                            Branch baseBranch,
                                            LogLevel errorLevel = LogLevel.Error )
    {
        Throw.CheckArgument( git.Repository == ((IBelongToARepository)branch).Repository
                             && git.Repository == ((IBelongToARepository)baseBranch).Repository );
        try
        {
            var r = git.Repository;
            var result = r.ObjectDatabase.MergeCommits( baseBranch.Tip, branch.Tip, _mergeOptions );
            if( result.Status != MergeTreeStatus.Succeeded )
            {
                monitor.Error( $"""
                    Unable to synchronize branch '{branch.FriendlyName}' on '{baseBranch.FriendlyName}' in '{git.DisplayPath}'.
                    Merge conflict must be manually resolved.
                    """ );
                return null;
            }
            else
            {
                var commit = r.ObjectDatabase.CreateCommit( author: git.Committer,
                                                            committer: git.Committer,
                                                            message: $"Synchronizing '{branch.FriendlyName}' on '{baseBranch.FriendlyName}'.",
                                                            result.Tree,
                                                            [baseBranch.Tip, branch.Tip],
                                                            prettifyMessage: true );
                r.Refs.UpdateTarget( branch.Reference, commit.Id );
                return r.Branches[branch.CanonicalName];
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                Error while synchronizing branch '{branch.FriendlyName}' on '{baseBranch.FriendlyName}'.
                This must be manually fixed.
                """, ex );
            return null;
        }
    }


    /// <summary>
    /// Tries to merge the <paramref name="branch"/> (the "ahead" branch) in its <paramref name="baseBranch"/>.
    /// On success, the ahead branch is deleted and a new, updated, base branch is returned.
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="git">The repository.</param>
    /// <param name="branch">The (ahead) branch.</param>
    /// <param name="baseBranch">The base branch.</param>
    /// <param name="errorLevel">Log level to use on error.</param>
    /// <returns>The updated base branch on success, null on error.</returns>
    public static Branch? IntegrateMerge( IActivityMonitor monitor,
                                          GitRepository git,
                                          Branch branch,
                                          Branch baseBranch,
                                          LogLevel errorLevel = LogLevel.Error )
    {
        Throw.CheckArgument( git.Repository == ((IBelongToARepository)branch).Repository
                             && git.Repository == ((IBelongToARepository)baseBranch).Repository );
        try
        {
            var r = git.Repository;

            if( branch.Tip.Tree.Id == baseBranch.Tip.Tree.Id )
            {
                // Nothing to merge (useless ahead case).
                // If the branch to remove is currently checked out, check out the base.
                if( branch.IsCurrentRepositoryHead
                    && !git.Checkout( monitor, baseBranch ) )
                {
                    return null;
                }
                // Removes it.
                r.Branches.Remove( branch );
                monitor.Trace( $"Branch '{branch.FriendlyName}' was useless, it has been deleted." );
                return baseBranch;
            }

            var result = r.ObjectDatabase.MergeCommits( branch.Tip, baseBranch.Tip, _mergeOptions );
            if( result.Status != MergeTreeStatus.Succeeded )
            {
                monitor.Error( $"""
                    Unable to integrate branch '{branch.FriendlyName}' into '{baseBranch.FriendlyName}' in '{git.DisplayPath}'.
                    Merge conflict must be manually resolved.
                    """ );
                return null;
            }
            else
            {
                var commit = r.ObjectDatabase.CreateCommit( author: git.Committer,
                                                            committer: git.Committer,
                                                            message: $"Integrating '{branch.FriendlyName}' in '{baseBranch.FriendlyName}'.",
                                                            result.Tree,
                                                            [branch.Tip, baseBranch.Tip],
                                                            prettifyMessage: true );
                r.Refs.UpdateTarget( baseBranch.Reference, commit.Id );
                // Not sure if a better way to refresh the Branch/Tip exists.
                baseBranch = r.Branches[baseBranch.CanonicalName];
                // If the branch to remove is currently checked out, check out the base.
                if( branch.IsCurrentRepositoryHead
                    && !git.Checkout( monitor, baseBranch ) )
                {
                    // If this fail, we are left with a "useless ahead branch" and this is fine.
                    return null;
                }
                // Removes it.
                r.Branches.Remove( branch );
                monitor.Trace( $"Branch '{branch.FriendlyName}' has been integrated in its base '{baseBranch.FriendlyName}' and deleted." );
                return baseBranch;
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                Error while synchronizing branch '{branch.FriendlyName}' on '{baseBranch.FriendlyName}'.
                This must be manually fixed.
                """, ex );
            return null;
        }
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
