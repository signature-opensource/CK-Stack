using CK.Core;
using CK.PerfectEvent;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin : PrimaryRepoPlugin<BranchModelInfo>
{
    readonly BranchNamespace _namespace;
    readonly VersionTagPlugin _versionTags;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;
    internal readonly ShallowSolutionPlugin _shallowSolution;
    readonly PerfectEventSender<FixWorkflowStartEventArgs> _onFixStart;

    Dictionary<string, SVersion>? _externalPackages;

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
        _namespace = new BranchNamespace( World.Name.LTSName,
                                          primaryContext.Configuration.XElement.Attribute( "Branches" )?.Value );
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
        bool hasIssue = false;
        foreach( var r in e.Repos )
        {
            var info = Get( monitor, r );
            if( info.HasIssue )
            {
                info.CollectIssues( monitor, e.ScreenType, e.Add );
                hasIssue = true;
            }
        }
        if( !hasIssue && ContentIssue != null )
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
                    success &= !tags.HasIssue;
                    var branch = Get( monitor, repo );
                    success &= !branch.HasIssue;
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
    /// This requires that <see cref="BranchModelInfo.HasIssue"/> is false. This can handle
    /// dirty working folders: if the specified branch is currently checked out in a repository,
    /// the working folder will be analyzed.
    /// </para>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <param name="branchName">The branch name.</param>
    /// <param name="pivots">
    /// Optional pivots of the graph.
    /// <list type="bullet">
    ///  <item>When empty, this is the same as the <see cref="World.GetAllDefinedRepo(IActivityMonitor)"/> list.</item>
    ///  <item>When not empty, it MUST be in strict increasing <see cref="Repo.Index"/> order.</item>
    /// </list>
    /// </param>
    /// <returns>The graph or null on error.</returns>
    public HotGraph? GetHotGraph( IActivityMonitor monitor, BranchName branchName, IReadOnlyList<Repo> pivots )
    {
        for( int i = 1; i < pivots.Count; i++ )
        {
            if( pivots[i-1].Index >= pivots[i].Index )
            {
                throw new ArgumentException( "Pivots must be in strict increasing index order." );
            }
        }
        var displayPivots = pivots.Count != 0 && pivots.Count != World.Layout.Count
                                ? $"'{pivots.Select( r => r.DisplayPath.Path ).Concatenate( "', '" )}'"
                                : "all repositories";
        using( monitor.OpenTrace( $"Computing Hot Graph from '{branchName}' for {displayPivots}." ) )
        {
            var allRepos = World.GetAllDefinedRepo( monitor );
            if( allRepos == null ) return null;
            if( !TryGetAllWithoutIssue( monitor, out _ )  )
            {
                return null;
            }
            // No pivot => all repositories are pivots.
            if( pivots.Count == 0 )
            {
                pivots = allRepos;
            }
            // Finds the most instable existing branch among the pivots (or among all the repositories).
            branchName = FindMostInstableBranchFrom( monitor, branchName, pivots );

            using( monitor.OpenInfo( pivots.Count == allRepos.Count
                                        ? $"Considering branch '{branchName}' from all {allRepos.Count} repositories."
                                        : $"Considering branch '{branchName}' from {pivots.Count} pivots out of {allRepos.Count} repositories." ) )
            {
                // We can now instantiate the graph object and add all the nodes that are the HotGraph.Solution
                // instances.
                var externalPackages = GetExternalPackages( monitor );
                if( externalPackages == null ) return null;

                var graph = new HotGraph( branchName, allRepos, pivots, _versionTags, externalPackages );
                bool hasPivots = graph.HasPivots;
                foreach( var repo in allRepos )
                {
                    var branchInfo = Get( monitor, repo );
                    var b = branchInfo.GetClosestActiveBranch( branchName );
                    Throw.DebugAssert( "There is no Branch Model issue: the closest branch necessarily exists.", b?.GitBranch != null );
                    var shallow = _shallowSolution.GetShallowSolution( monitor, repo, b.GitDevBranch ?? b.GitBranch );
                    if( shallow == null ) return null;
                    if( !graph.AddSolution( monitor, repo, b, shallow, hasPivots ? pivots.Contains( repo ) : false ) )
                    {
                        return null;
                    }
                }
                // The solutions have been successfully added. The mappings from "package name" (that are the project names)
                // to the solutions are non ambiguous. We can start the topological sort.
                // The sort starts with the pivots (this will walk all the dependencies and sets the IsPivotUpstream).
                return graph.TopologicalSort( monitor )
                        ? graph
                        : null;
            }
        }
    }

    Dictionary<string, SVersion>? GetExternalPackages( IActivityMonitor monitor )
    {
        try
        {
            return _externalPackages ??= PrimaryPluginContext.Configuration.XElement
                                                    .Elements( "Packages" )
                                                    .Elements( "Package" )
                                                    .ToDictionary( e => (string)e.Attribute( XNames.Name )!,
                                                                    e => SVersion.Parse( (string)e.Attribute( XNames.Version )! ) );
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                Unable to read <Packages> element from <BranchModel> configuration.
                Expecting:
                <Packages>
                    <Package Name="..." Version="..." />
                </Packages>
                Configuration is:
                {PrimaryPluginContext.Configuration.XElement}
                """, ex );
            return null;
        }
    }

    BranchName FindMostInstableBranchFrom( IActivityMonitor monitor, BranchName branchName, IEnumerable<Repo> pivots )
    {
        BranchName? mostInstable = null;
        foreach( var p in pivots )
        {
            var closest = Get( monitor, p ).GetClosestActiveBranch( branchName );
            Throw.DebugAssert( "There is no Branch Model issue: the closest git branch necessarily exists.", closest?.GitBranch != null );
            if( closest.BranchName != branchName )
            {
                monitor.Info( $"Repository '{p.DisplayPath}' has no branch '{branchName}', considering the closest one that is '{closest.BranchName}'." );
            }
            // Finding the most instable branch.
            if( mostInstable == null || mostInstable.Index < closest.BranchName.Index )
            {
                mostInstable = closest.BranchName;
            }
        }
        Throw.DebugAssert( mostInstable != null );
        if( mostInstable != branchName )
        {
            monitor.Info( $"Eventually considering branch '{mostInstable}'." );
            branchName = mostInstable;
        }
        return branchName;
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

