using CK.Core;
using CK.PerfectEvent;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;


public sealed partial class BranchModelPlugin : PrimaryRepoPlugin<BranchModelInfo>
{
    readonly BranchNamespace _namespace;
    readonly VersionTagPlugin _versionTags;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ShallowSolutionPlugin _shallowSolution;
    readonly PerfectEventSender<FixWorkflowStartEventArgs> _onFixStart;

    /// <summary>
    /// This is a primary plugin.
    /// </summary>
    public BranchModelPlugin( PrimaryPluginContext primaryContext,
                              VersionTagPlugin versionTags,
                              ReleaseDatabasePlugin releaseDatabase,
                              ArtifactHandlerPlugin artifactHandler,
                              ShallowSolutionPlugin shallowSolution )
        : base( primaryContext )
    {
        _namespace = new BranchNamespace( World.Name.LTSName );
        World.Events.Issue += IssueRequested;
        _versionTags = versionTags;
        _releaseDatabase = releaseDatabase;
        _artifactHandler = artifactHandler;
        _shallowSolution = shallowSolution;
        _onFixStart = new PerfectEventSender<FixWorkflowStartEventArgs>();
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
    public BranchNamespace BranchNamespace => _namespace;

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
        if( !_namespace.Branches.TryGetValue( branchName, out var exists ) )
        {
            var branchNames = _namespace.Branches.Values.Select( b => b.Name );
            monitor.Error( $"""
                Invalid branch '{branchName}'.
                Supported branches are '{branchNames.Order().Concatenate( "', '" )}'.
                """ );
        }
        return exists;
    }

    /// <summary>
    /// Basic preconditions are strict:
    /// <list type="bullet">
    ///     <item>No repo must be dirty.</item>
    ///     <item>No repo's VersionTagInfo must have issues.</item>
    ///     <item>No repo's BranchModelInfo must have issues.</item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="plannedAction">The planned action description. Example: "building" or "starting a fix".</param>
    /// <param name="allRepos">Outputs all the <see cref="Repo"/>.</param>
    /// <returns>True on success, false on error.</returns>
    public bool CheckBasicPreconditions( IActivityMonitor monitor,
                                         string plannedAction,
                                         [NotNullWhen( true )] out IReadOnlyList<Repo>? allRepos )
    {
        using( monitor.OpenTrace( $"Checking World's global state." ) )
        {
            bool success = true;
            allRepos = World.GetAllDefinedRepo( monitor );
            if( allRepos == null ) return false;
            foreach( var repo in allRepos )
            {
                if( !repo.GitRepository.CheckCleanCommit( monitor ) )
                {
                    success = false;
                }
                else
                {
                    var tags = _versionTags.Get( monitor, repo );
                    success &= !tags.HasIssues;
                    var branch = Get( monitor, repo );
                    success &= !branch.HasIssues;
                }
            }
            if( !success )
            {
                monitor.Error( $"Please fix any issue before {plannedAction}." );
            }
            return success;
        }
    }

    /// <summary>
    /// Gets the <see cref="HotGraph"/> for a <see cref="BranchName"/>.
    /// <para>
    /// This calls <see cref="CheckBasicPreconditions(IActivityMonitor, string, out IReadOnlyList{Repo}?)"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="pivot">Optional pivot of the graph.</param>
    /// <param name="branchName">The branch name.</param>
    /// <returns>The graph or null on error.</returns>
    public HotGraph? GetHotGraph( IActivityMonitor monitor, Repo? pivot, BranchName branchName )
    {
        using( monitor.OpenTrace( pivot != null
                                    ? $"Computing Hot graph for '{branchName}' from '{pivot.DisplayPath}'."
                                    : $"Computing Hot graph for '{branchName}'.") )
        {
            var allRepos = World.GetAllDefinedRepo( monitor );
            if( allRepos == null ) return null;
            using( monitor.OpenTrace( $"Checking Branch Model issues." ) )
            {
                bool success = true;
                foreach( var repo in allRepos )
                {
                    success &= !Get( monitor, repo ).HasIssues;
                }
                if( !success )
                {
                    monitor.Error( $"Please fix any issue before continuing." );
                }
            }
            // If we have a pivot, we align the requested branch name on the actual
            // best branch that this pivot contains.
            if( pivot != null )
            {
                var branchInfo = Get( monitor, pivot );
                var b = branchInfo.FindClosestGitBranch( branchName );
                Throw.DebugAssert( "There is no Branch Model issue: the closest git branch necessarily exists.", b != null );
                var actual = _namespace.Branches[b.FriendlyName];
                if( actual != branchName )
                {
                    monitor.Info( $"Repository '{pivot.DisplayPath}' has no branch '{branchName}', considering the closest on that is '{actual}'." );
                    branchName = actual;
                }
            }
            // We can now instantiate the graph object and add all the nodes that are the HotGraph.Solution
            // instances.
            var graph = new HotGraph( this, branchName, allRepos.Count, pivot );
            foreach( var repo in allRepos )
            {
                var branchInfo = Get( monitor, repo );
                var b = branchInfo.FindClosestGitBranch( branchName );
                Throw.DebugAssert( "There is no Branch Model issue: the closest git branch necessarily exists.", b != null );
                var actual = _namespace.Branches[b.FriendlyName];
                var shallow = _shallowSolution.GetShallowSolution( monitor, repo, b );
                if( shallow == null ) return null;
                if( !graph.AddSolution( monitor, repo, branchInfo, actual, shallow ) )
                {
                    return null;
                }
            }
            // The solutions have been successfully added. The mappings from "package name" (that are the project names)
            // to the solutions are non ambiguous. We can start the topological sort.
            // The sort starts with the pivot if it exits (this will walk all the dependencies and sets the IsPivotUpstream).
            return graph.Sort( monitor ) ? graph : null;
        }
    }



    protected override BranchModelInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var git = repo.GitRepository.Repository;
        var gitRoot = repo.GitRepository.GetBranch( monitor, _namespace.Root.Name, missingLocalAndRemote: LogLevel.None );
        var root = new HotBranch( _namespace.Root, null, null, gitRoot, null );
        if( gitRoot == null )
        {
            if( PrimaryPluginContext.Command is not CKliIssue )
            {
                monitor.Warn( $"Missing '{root.BranchName}' branch in '{repo.DisplayPath}'. Use 'ckli issue' for details." );
            }
            // The worst issue: no "stable" branch. This has to be resolved before doing anything else.
            return new BranchModelInfo( repo, _namespace, root );
        }
        // We have our hot "stable".
        var index = new Dictionary<string, HotBranch>( _namespace.Branches.Count );
        index.Add( root.BranchName.Name, root );
        List<HotBranch>? removable = null;
        List<HotBranch>? desynchronized = null;
        List<HotBranch>? unrelated = null;
        CreateChildren( monitor, root, repo, git, index, ref removable, ref desynchronized, ref unrelated );
        Throw.DebugAssert( index.Count == _namespace.Branches.Count );
        // Traversal has been done top-down. If branches can be removed this must be done bottom-up
        // so we reverse the list.
        if( removable != null ) removable.Reverse();
        // Currently we consider that removable branches is an issue.
        // This is clearly a bit too strong but simplifies the build initialization.
        // To make this optional, The HotBranch.EnsureGitBranch() must be enhanced to reposition the
        // branch tip.
        if( (unrelated != null || desynchronized != null || removable != null) && PrimaryPluginContext.Command is not CKliIssue )
        {
            monitor.Warn( $"Repository '{repo.DisplayPath}' has branch related issues. Use 'ckli issue' for details." );
        }
        return new BranchModelInfo( repo, _namespace, root, index, removable, desynchronized, unrelated );

        static void CreateChildren( IActivityMonitor monitor,
                                    HotBranch parent,
                                    Repo repo,
                                    Repository git,
                                    Dictionary<string, HotBranch> index,
                                    ref List<HotBranch>? removable,
                                    ref List<HotBranch>? desynchronized,
                                    ref List<HotBranch>? unrelated )
        {
            var baseBranch = parent.GitBranch != null ? parent : parent.ExistingBaseBranch;
            foreach( var childName in parent.BranchName.Children )
            {
                var gitBranch = repo.GitRepository.GetBranch( monitor, childName.Name, missingLocalAndRemote: LogLevel.None );
                HistoryDivergence? div = gitBranch != null && baseBranch?.GitBranch != null
                                            ? git.ObjectDatabase.CalculateHistoryDivergence( gitBranch.Tip, baseBranch.GitBranch.Tip )
                                            : null;
                var b = new HotBranch( childName, parent, baseBranch, gitBranch, div );
                index.Add( childName.Name, b );
                if( div != null && div.CommonAncestor == null )
                {
                    unrelated ??= new List<HotBranch>();
                    unrelated.Add( b );
                }
                else if( b.IsOrphanDevBranch || b.IsIntegratedBranch )
                {
                    removable ??= new List<HotBranch>();
                    removable.Add( b );
                }
                else if( b.IsDesynchronizedBranch )
                {
                    desynchronized ??= new List<HotBranch>();
                    desynchronized.Add( b );
                }
                if( childName.HasChild )
                {
                    CreateChildren( monitor, b, repo, git, index, ref removable, ref desynchronized, ref unrelated );
                }
            }
        }
    }

    /// <summary>
    /// Tries to parse "fix/v<paramref name="major"/>.<paramref name="minor"/>".
    /// </summary>
    /// <param name="s">The name to parse.</param>
    /// <param name="major">The major version to fix.</param>
    /// <param name="minor">The minor version to fix.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParseBranchFixName( ReadOnlySpan<char> s, int major, out int minor )
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

