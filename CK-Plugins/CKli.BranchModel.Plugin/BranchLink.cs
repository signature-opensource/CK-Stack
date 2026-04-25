using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// A link associates a <see cref="Branch"/> to an optional <see cref="Ahead"/> branch.
/// Ahead must be based on the Branch and have commits ahead or not exist at all (or
/// the <see cref="Issue"/> should be fixed).
/// <para>
/// This object is immutable. The <see cref="Create(Branch, Branch)"/> and <see cref="Create(Branch, string)"/> factory
/// methods are the only way to create a new link. Then the mutation methods can be used.
/// </para>
/// </summary>
public sealed partial class BranchLink
{
    static readonly MergeTreeOptions _mergeTreeOptions = new MergeTreeOptions()
    {
        FailOnConflict = true,
        SkipReuc = true
    };
    static readonly MergeOptions _mergeOptions = new MergeOptions()
    {
        FailOnConflict = true,
        SkipReuc = true,
        FastForwardStrategy = FastForwardStrategy.NoFastForward
    };

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
            // If the ahead branch is not ahead, it is useless. We also consider the case of the exact same content:
            // the "ahead empty commit" is here... it is useless...
            if( _aheadBy == 0 || _branch.Tip.Tree.Sha == _ahead.Tip.Tree.Sha )
            {
                // ...but if the ahead branch is checked out and the working folder is dirty
                // then we cannot say that the ahead branch is useless! 
                if( !_ahead.IsCurrentRepositoryHead
                    || !RepositoryOf( _branch ).RetrieveStatus( new StatusOptions() { IncludeIgnored = false } ).IsDirty )
                {
                    return IssueKind.Useless;
                }
                return IssueKind.None;
            }
            if( _behindBy != 0 )
            {
                if( _behindBy == -1 )
                {
                    Throw.DebugAssert( _aheadBy == -1 );
                    return IssueKind.Unrelated;
                }
                return IssueKind.Desynchronized;
            }
            Throw.DebugAssert( _aheadBy > 0 );
            return IssueKind.None;
        }
    }

    /// <summary>
    /// Integrates the <see cref="Ahead"/> branch into the <see cref="Branch"/> and
    /// deletes it. If Ahead is null, nothing is done and this link is returned as-is.
    /// On success, a new <see cref="BranchLink"/> (that replaces this one) is returned.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <returns>An updated link (replaces this one) on success, null on error.</returns>
    public BranchLink? IntegrateAhead( IActivityMonitor monitor, GitRepository repo )
    {
        if( _ahead == null ) return this;
        var newBase = IntegrateMerge( monitor, repo, _ahead, _branch );
        return newBase == null
                ? null
                : new BranchLink( newBase, null, _aheadName, 0, 0 );
    }

    /// <summary>
    /// Synchronizes this <see cref="Branch"/> into <see cref="Ahead"/> when <see cref="Issue"/> is <see cref="IssueKind.Desynchronized"/>.
    /// WHen Issue is <see cref="IssueKind.Useless"/>, nothing is done. When Issue is <see cref="IssueKind.Unrelated"/>, an error is logged
    /// and null is returned.
    /// <para>
    /// On success, a new <see cref="BranchLink"/> (that replaces this one) is returned.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <returns>An updated link (replaces this one) on success, null on error.</returns>
    public BranchLink? SynchronizeAhead( IActivityMonitor monitor, GitRepository repo )
    {
        var issue = Issue;
        if( issue is IssueKind.Desynchronized )
        {
            Throw.DebugAssert( _ahead != null );
            var newAhead = SynchronizeMerge( monitor, repo, _ahead, _branch );
            return newAhead == null
                    ? null
                    : newAhead == _ahead
                        ? this
                        : new BranchLink( _branch, newAhead, _aheadName, _aheadBy, 0 );
        }
        if( issue is IssueKind.Unrelated )
        {
            Throw.DebugAssert( _ahead != null );
            monitor.Error( $"""
                            Branch '{_ahead.FriendlyName}' is independent of its base '{_branch.FriendlyName}' (no common ancestor).
                            This is an unexpected situation that must be fixed manually.
                            """ );
            return null;
        }
        return this;
    }

    /// <summary>
    /// Refreshes the link: if <see cref="Branch"/> doesn't exist anymore, null is returned.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <returns>This link if no change, a refreshed one or null if <see cref="Branch"/> disappeared.</returns>
    public BranchLink? Refresh( GitRepository repo )
    {
        Throw.CheckArgument( repo.Repository == RepositoryOf( Branch ) );
        var newBranch = repo.Repository.Branches[_branch.CanonicalName];
        if( newBranch == null ) return null;
        var newAhead = repo.Repository.Branches[_aheadName];
        return newBranch.Tip.Sha == _branch.Tip.Sha && newAhead?.Tip.Sha == _ahead?.Tip.Sha
                ? this
                : newAhead != null
                    ? Create( newBranch, newAhead )
                    : new BranchLink( newBranch, null, _aheadName, 0, 0 );
    }

    /// <summary>
    /// Collects this link's issue if any.
    /// </summary>
    /// <param name="issues">The collector for issues.</param>
    internal void CollectIssue( IssueBuilder issues )
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
    /// Ensures that <see cref="Ahead"/> branch exists.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="withEmptyInitializationCommit">True to create new empty initialization commit if the ahead branch is missing.</param>
    /// <returns>An updated link (replaces this one).</returns>
    public BranchLink EnsureAhead( GitRepository repo, bool withEmptyInitializationCommit = false )
    {
        Throw.CheckArgument( repo.Repository == RepositoryOf( Branch ) );
        if( _ahead != null ) return this;
        Branch ahead = CreateAheadBranch( repo, _branch.Tip, _aheadName, withEmptyInitializationCommit );
        return new BranchLink( _branch, ahead, _aheadName, 1, 0 );
    }

    /// <summary>
    /// Commits the <see cref="Ahead"/> branch that must be currently checked out otherwise
    /// a <see cref="InvalidOperationException"/> is thrown.
    /// On success, return an updated link.
    /// <para>
    /// Note that there may have been commits done before this one: even if there's
    /// nothing to commit, the returned link is up to date.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>An updated link (replaces this one) on success, null on error.</returns>
    public BranchLink? CommitAhead( IActivityMonitor monitor, GitRepository repo, string message )
    {
        Throw.CheckState( Ahead != null && Ahead.IsCurrentRepositoryHead );
        Throw.CheckArgument( repo.Repository == RepositoryOf( Ahead ) );
        return repo.Commit( monitor, message, CommitBehavior.CreateNewCommit ) switch
        {
            CommitResult.Error => null,
            _ => Create( _branch, repo.Repository.Head )
        };
    }

    static IRepository RepositoryOf( IBelongToARepository o ) => o.Repository;

    internal static Branch CreateAheadBranch( GitRepository git, Commit baseCommit, string aheadBranchName, bool withEmptyInitializationCommit )
    {
        var repository = git.Repository;
        Throw.DebugAssert( repository == RepositoryOf( baseCommit ) );
        // Lookup for an existing "origin" remote.
        // (Reproduces GitRepository.GetBranch method - AROBAS).
        string remoteName = "origin/" + aheadBranchName;
        var remote = repository.Branches[remoteName];
        if( remote != null )
        {
            var b = repository.Branches.Add( aheadBranchName, remote.Tip );
            b = repository.Branches.Update( b, u => u.TrackedBranch = remote.CanonicalName );
            return b;
        }
        // No corresponding remote branch: "empty ahead commit" if withEmptyInitializationCommit.
        var c = withEmptyInitializationCommit
                    ? repository.ObjectDatabase.CreateCommit( baseCommit.Author,
                                                              git.Committer, $"""
                                                              Initializing '{aheadBranchName}'.
                                                              
                                                              """,
                                                              baseCommit.Tree,
                                                              [baseCommit],
                                                              prettifyMessage: false )
                    : baseCommit;
        return repository.Branches.Add( aheadBranchName, c );
    }

    /// <summary>
    /// Tries to merge the <paramref name="baseBranch"/> in the <paramref name="aheadBranch"/> (the "ahead" branch).
    /// On success, the updated ahead branch is returned.
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="git">The repository.</param>
    /// <param name="aheadBranch">The ahead branch.</param>
    /// <param name="baseBranch">The base branch.</param>
    /// <param name="errorLevel">Log level to use on error.</param>
    /// <returns>The updated ahead branch on success, null on error.</returns>
    public static Branch? SynchronizeMerge( IActivityMonitor monitor,
                                            GitRepository git,
                                            Branch aheadBranch,
                                            Branch baseBranch,
                                            LogLevel errorLevel = LogLevel.Error )
    {
        Throw.CheckArgument( git.Repository == RepositoryOf( aheadBranch ) && git.Repository == RepositoryOf( baseBranch ) );

        if( !git.MergeBranch( monitor, ref aheadBranch, baseBranch ) )
        {
            return null;
        }
        return aheadBranch;
    }

    /// <summary>
    /// Tries to merge the <paramref name="aheadBranch"/> into its <paramref name="baseBranch"/>.
    /// On success, the ahead branch is deleted and a new, updated, base branch is returned.
    /// <para>
    /// If the ahead branch is checked out then the base branch is automatically checked out. 
    /// </para>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="git">The repository.</param>
    /// <param name="aheadBranch">The (ahead) branch.</param>
    /// <param name="baseBranch">The base branch.</param>
    /// <param name="removeBranch">False to not delete <paramref name="aheadBranch"/> on success.</param>
    /// <param name="errorLevel">Log level to use on error.</param>
    /// <returns>The updated base branch on success, null on error.</returns>
    public static Branch? IntegrateMerge( IActivityMonitor monitor,
                                          GitRepository git,
                                          Branch aheadBranch,
                                          Branch baseBranch,
                                          bool removeBranch = true,
                                          LogLevel errorLevel = LogLevel.Error )
    {
        Throw.CheckArgument( git.Repository == RepositoryOf( aheadBranch ) && git.Repository == RepositoryOf( baseBranch ) );

        bool aheadIsCheckedOut = aheadBranch.IsCurrentRepositoryHead;

        if( !git.MergeBranch( monitor, ref baseBranch, aheadBranch ) )
        {
            return null;
        }
        if( aheadIsCheckedOut && !git.Checkout( monitor, baseBranch ) )
        {
            return null;
        }
        if( removeBranch && !git.DeleteBranch( monitor, aheadBranch, DeleteGitBranchMode.WithTrackedBranch ) )
        {
            return null;
        }
        return baseBranch;
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
    public static BranchLink Create( Branch b, Branch ahead )
    {
        CheckValidBranchReferenceArgument( b );
        CheckValidBranchReferenceArgument( ahead );
        var r = ((IBelongToARepository)b).Repository;
        int aheadBy;
        int behindBy;
        var d = r.ObjectDatabase.CalculateHistoryDivergence( ahead.Tip, b.Tip );
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
    public static BranchLink Create( Branch b, string aheadName )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( aheadName );
        var r = ((IBelongToARepository)b).Repository;
        CheckValidBranchReferenceArgument( b );
        var ahead = r.Branches[aheadName];
        return ahead != null
                ? Create( b, ahead )
                : new BranchLink( b, null, aheadName, 0, 0 );
    }

    /// <summary>
    /// Helper that throws a <see cref="ArgumentException"/> if the branch is null or is invalid.
    /// </summary>
    /// <param name="b">The branch to check.</param>
    public static void CheckValidBranchReferenceArgument( Branch b )
    {
        Throw.CheckNotNullArgument( b );
        var repo = ((IBelongToARepository)b).Repository;
        var tip = b.Tip.Sha;
        var actualTip = repo.Refs[b.CanonicalName]?.TargetIdentifier;
        if( actualTip != tip )
        {
            throw new ArgumentException( $"Invalid branch '{b}': branch's tip is '{tip}' but current branch references '{actualTip}'." );
        }
    }

}
