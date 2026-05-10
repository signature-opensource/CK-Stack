using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin : PrimaryRepoPlugin<BranchModelInfo>
{
    readonly BranchNamespace _namespace;
    internal readonly ShallowSolutionPlugin _shallowSolution;

    /// <summary>
    /// This is a primary plugin.
    /// </summary>
    public BranchModelPlugin( PrimaryPluginContext primaryContext,
                              ShallowSolutionPlugin shallowSolution )
        : base( primaryContext )
    {
        _namespace = new BranchNamespace( World.Name.LTSName,
                                          primaryContext.Configuration.XElement.Attribute( XNames.Branches )?.Value );
        World.Events.Issue += IssueRequested;
        _shallowSolution = shallowSolution;
    }

    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        bool hasSevereIssue = false;
        foreach( var r in e.Repos )
        {
            var info = Get( monitor, r );
            info.CollectIssues( monitor, e.ScreenType, e.Add, out hasSevereIssue );
        }
        if( !hasSevereIssue && ContentIssue != null )
        {
            using( monitor.OpenInfo( "Raising ContentIssue event." ) )
            {
                foreach( var r in e.Repos )
                {
                    var info = Get( monitor, r );
                    var issueBuilder = new ContentIssueBuilder( info, RaiseContentIssue );
                    if( !issueBuilder.CreateIssue( monitor, e.ScreenType, e.Add ) )
                    {
                        monitor.CloseGroup( $"ContentIssue event handling failed." );
                        // Stop on the first error.
                        break;
                    }
                }
            }
        }
    }

    bool RaiseContentIssue( IActivityMonitor monitor, ContentIssueEvent e )
    {
        Throw.DebugAssert( ContentIssue != null );
        bool success = true;
        using( monitor.OnError( () => success = false ) )
        {
            ContentIssue( e );
        }
        return success;
    }

    /// <summary>
    /// Gets the branch model.
    /// </summary>
    public BranchNamespace BranchNamespace => _namespace;

    /// <summary>
    /// Raised when repository content issues must be detected in the hot zone.
    /// </summary>
    public event Action<ContentIssueEvent>? ContentIssue;

    /// <summary>
    /// Finds the <paramref name="branchName"/> in the <see cref="BranchNamespace"/> or emits an error
    /// if this is not a valid name.
    /// </summary>
    /// <param name="monitor">The monitor to emit the error.</param>
    /// <param name="branchName">The branch name to lookup.</param>
    /// <returns>The name or null on error.</returns>
    public BranchName? GetValidBranchName( IActivityMonitor monitor, string branchName )
    {
        // If we are not on a known branch (defined by the Branch Model), give up.
        if( !_namespace.ByName.TryGetValue( branchName, out var exists ) )
        {
            monitor.Error( $"""
                Invalid branch '{branchName}'.
                Supported branches are '{_namespace.Branches.Select( b => b.Name ).Concatenate( "', '" )}'.
                """ );
        }
        return exists;
    }

    protected override BranchModelInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var info = new BranchModelInfo( repo, _namespace, this );
        var git = repo.GitRepository.Repository;
        var root = HotBranch.Create( monitor, info, repo.GitRepository, _namespace.Root );
        if( root.GitBranch == null )
        {
            if( PrimaryPluginContext.Command is not CKliIssue )
            {
                monitor.Warn( $"Missing '{root.BranchName}' branch in '{repo.DisplayPath}'. Use 'ckli issue' for details." );
            }
            // The worst issue: no root "stable" branch. This has to be resolved before doing anything else.
            info.Initialize( [root], hasIssue: true );
            return info;
        }
        // We have our hot root "stable" branch.
        bool hasIssue = root.HasIssue;
        var hotBranches = new HotBranch[_namespace.Branches.Length];
        hotBranches[0] = root;
        for( int i = 1; i < hotBranches.Length; ++i )
        {
            var branchName = _namespace.Branches[i];
            var b = HotBranch.Create( monitor, info, repo.GitRepository, branchName );
            hasIssue |= b.HasIssue;
            hotBranches[i] = b;
        }
        info.Initialize( ImmutableCollectionsMarshal.AsImmutableArray( hotBranches ), hasIssue );
        return info;
    }

    /// <summary>
    /// Tries to parse "fix/v<paramref name="major"/>.<paramref name="minor"/>".
    /// </summary>
    /// <param name="s">The name to parse.</param>
    /// <param name="major">The major version to fix.</param>
    /// <param name="minor">The minor version to fix.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParseBranchFixName( ReadOnlySpan<char> s, out int major, out int minor )
    {
        major = 0;
        minor = 0;
        return s.TryMatch( "fix/" )
               && s.TryMatch( 'v' )
               && s.TryMatchInteger( out major )
               && major >= 0
               && s.TryMatch( '.' )
               && s.TryMatchInteger( out minor )
               && minor >= 0
               && s.Length == 0;
    }
}

