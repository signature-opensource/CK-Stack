using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System;
using System.Linq;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

public sealed class BranchModelPlugin : PrimaryRepoPlugin<BranchModelInfo>
{
    readonly BranchTree _branchTree;
    readonly VersionTagPlugin _versionTags;

    /// <summary>
    /// This is a primary plugin.
    /// </summary>
    public BranchModelPlugin( PrimaryPluginContext primaryContext, VersionTagPlugin versionTags )
        : base( primaryContext )
    {
        _branchTree = new BranchTree( World.Name.LTSName );
        World.Events.Issue += IssueRequested;
        _versionTags = versionTags;
    }

    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        foreach( var r in e.Repos )
        {
            var tags = _versionTags.Get( monitor, r );
            Get( monitor, r ).CollectIssues( monitor, tags, e.ScreenType, e.Add );
        }
    }

    /// <summary>
    /// Gets the branch model.
    /// </summary>
    public BranchTree BranchTree => _branchTree;

    protected override BranchModelInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var info = new BranchModelInfo( repo, _branchTree );
        info.Initialize( monitor );
        return info;
    }

    [Description( "Ensures that a 'vMajor.Minor/fix' branch exists in the repository and checkouts it." )]
    [CommandPath( "branch fix" )]
    public bool BranchFix( IActivityMonitor monitor,
                           CKliEnv context,
                           [Description( "The Major or Major.Minor for which a fix must be produced." )]
                           string version,
                           [Description( "Don't initially fetch 'origin' repository." )]
                           bool noFetch,
                           bool moveBranch )
    {
        // Parses the Major.Minor. Minor is -1 if only Major is specified (we'll consider the max Minor).
        if( !Parse( version, out int major, out int minor ) )
        {
            monitor.Error( $"Invalid version '{version}'. Must be a major number, major.minor numbers optionally prefixed with 'v'." );
            return false;
        }
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null )
        {
            return false;
        }
        // Fetch the repo before analyzing versions.
        if( !noFetch && !repo.Fetch( monitor ) )
        {
            return false;
        }
        // Find the commit that must be fixed. This can perfectly be a +Fake one.
        var versionInfo = _versionTags.Get( monitor, repo );
        var toFix = versionInfo.LastStables.Where( c => c.Version.Major == major && (minor == -1 || c.Version.Minor == minor) ).Max();
        if( toFix == null )
        {
            monitor.Error( minor != -1
                            ? $"Unable to find any version to fix for 'v{major}.{minor}'."
                            : $"Unable to find any version to fix for 'v{major}'." );
            return false;
        }
        // The branch name is known.
        var branchName = $"v{toFix.Version.Major}.{toFix.Version.Minor}/fix";
        monitor.Info( $"Found '{toFix}' as the base commit. Ensuring branch '{branchName}'." );

        // Find an existing branch.
        var r = repo.GitRepository.Repository;
        var bFix = repo.GitRepository.GetBranch( monitor, branchName, LogLevel.Info );
        if( bFix != null )
        {
            // When bFix.Tip.Tree.Sha == toFix.ContentSha, we are in the nominal case:
            // the commit referenced by the /fix branch contains the code to fix.
            if( bFix.Tip.Tree.Sha != toFix.ContentSha )
            {
                // The /fix branch must contain the commit to fix.
                var versionedParent = versionInfo.FindFirst( bFix.Commits, out _ );
                if( versionedParent != toFix )
                {
                    if( !moveBranch )
                    {
                        monitor.Error( $"""
                            Branch '{branchName}' doesn't contain the commit '{toFix.Sha}' for the version '{toFix.Version.ParsedText}' to fix.
                            Use --move-branch flag to move the branch on the commit to fix.
                            """ );
                        return false;
                    }
                    monitor.Info( $"Moving branch '{branchName}' to {toFix.Sha}." );
                    bFix = null;
                }
            }
        }
        // Provide an empty commit to the developer so that the branch is not on the existing versioned commit.
        if( bFix == null || bFix.Tip.Sha == toFix.Commit.Sha )
        {
            var c = r.ObjectDatabase.CreateCommit( toFix.Commit.Author,
                                                   context.Committer,
                                                   $"Starting '{branchName}' (this commit can be amended).",
                                                   toFix.Commit.Tree,
                                                   [toFix.Commit],
                                                   prettifyMessage: false );
            // Create or update the /fix branch.
            repo.GitRepository.Repository.Branches.Add( branchName, c, allowOverwrite: true );
        }
        return true;

        static bool Parse( ReadOnlySpan<char> s, out int major, out int minor )
        {
            minor = -1;
            s.TryMatch( 'v' );
            if( s.TryMatchInteger( out major ) && major >= 0 )
            {
                if( s.TryMatch( '.' ) && (!s.TryMatchInteger( out minor ) || minor < 0) )
                {
                    return false;
                }
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Tries to parse "v<paramref name="major"/>.<paramref name="minor"/>/fix".
    /// </summary>
    /// <param name="branchName">The name to parse.</param>
    /// <param name="major">The major version to fix.</param>
    /// <param name="minor">The minor version to fix.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParseBranchFixName( string branchName, out int major, out int minor )
    {
        major = 0;
        minor = 0;
        var s = branchName.AsSpan();
        return s.TryMatch( 'v' )
               && s.TryMatchInteger( out major )
               && major >= 0
               && s.TryMatch( '.' )
               && s.TryMatchInteger( out minor )
               && minor >= 0
               && s.TryMatch( "/fix" )
               && s.Length == 0;
    }
}

