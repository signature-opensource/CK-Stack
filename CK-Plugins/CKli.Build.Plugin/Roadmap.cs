using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

/// <summary>
/// Augments a <see cref="HotGraph"/> and its <see cref="HotGraph.Solution"/> with versions and build actions.
/// </summary>
public sealed partial class Roadmap
{
    readonly VersionTagPlugin _versionTags;
    readonly HotGraph _graph;
    readonly bool _isPullBuild;
    readonly bool _isDevBuild;
    readonly ImmutableArray<BuildSolution> _orderedSolutions;
    readonly ImmutableArray<BuildSolution> _pivots;
    int _buildSolutionCount;

    internal Roadmap( VersionTagPlugin versionTags, HotGraph graph, bool isPullBuild, bool isDevBuild )
    {
        _versionTags = versionTags;
        _graph = graph;
        _isPullBuild = isPullBuild;
        _isDevBuild = isDevBuild;
        var buildSolutions = new BuildSolution[graph.Solutions.Count];
        var pivots = graph.HasPivots ? new BuildSolution[graph.Pivots.Count] : buildSolutions;
        int iPivot = 0;
        foreach( var s in graph.OrderedSolutions )
        {
            Throw.DebugAssert( "one solution is a pivot => the graph has pivots.", !s.IsPivot || graph.HasPivots );
            var sR = new BuildSolution( this, s );
            buildSolutions[s.OrderedIndex] = sR;
            if( s.IsPivot )
            {
                pivots[iPivot++] = sR;
            }
        }
        _orderedSolutions = ImmutableCollectionsMarshal.AsImmutableArray( buildSolutions );
        _pivots = ImmutableCollectionsMarshal.AsImmutableArray( pivots );
    }

    /// <summary>
    /// Gets the build solutions (ordered by <see cref="HotGraph.Solution.OrderedIndex"/>).
    /// </summary>
    public ImmutableArray<BuildSolution> OrderedSolutions => _orderedSolutions;

    /// <summary>
    /// Gets the pivots solutions if some has been specified.
    /// This is never empty (no pivot means all solutions are pivot).
    /// <para>
    /// This list is ordered by <see cref="Repo.Index"/>.
    /// </para>
    /// </summary>
    public ImmutableArray<BuildSolution> Pivots => _pivots;

    /// <summary>
    /// Gets whether there are pivots: <see cref="Pivots"/> are not the same as <see cref="OrderedSolutions"/>.
    /// </summary>
    public bool HasPivots => _graph.HasPivots;

    /// <summary>
    /// Gets the count of <see cref="OrderedSolutions"/> that have true <see cref="BuildSolution.MustBuild"/>.
    /// </summary>
    public int SolutionBuildCount => _buildSolutionCount;

    /// <summary>
    /// Gets whether this is a build on the "dev/" branch (produces CI packages).
    /// </summary>
    public bool IsDevBuild => _isDevBuild;

    internal bool Initialize( IActivityMonitor monitor )
    {
        if( !_versionTags.TryGetAllWithoutIssue( monitor, out var allVersionTags ) )
        {
            return false;
        }
        foreach( var s in _pivots )
        {
            if( !s.InitializeFromPivot( monitor, allVersionTags ) )
            {
                return false;
            }
        }
        foreach( var s in _orderedSolutions )
        {
            if( !s.InitializeFinal( monitor, allVersionTags ) )
            {
                return false;
            }
        }
        Throw.DebugAssert( _orderedSolutions.Count( s => s.MustBuild ) == _buildSolutionCount );
        int idxNumber = 1;
        foreach( var s in _orderedSolutions )
        {
            if( s.MustBuild ) s.BuildNumber = idxNumber++;
        }
        return true;
    }

    internal Task<bool> BuildAsync( IActivityMonitor monitor, CKliEnv context, BuildPlugin buildPlugin, bool? runTest, int maxDop )
    {
        if( _buildSolutionCount == 0 )
        {
            monitor.Info( ScreenType.CKliScreenTag, "No repositories need to be built." );
            return Task.FromResult( true );
        }

        bool hasDirtyFolder = false;
        var mapping = new ConcurrentDictionary<string,SVersion>( StringComparer.OrdinalIgnoreCase );
        foreach( var s in _orderedSolutions )
        {
            if( s.Repo.GitStatus.IsDirty )
            {
                hasDirtyFolder = true;
                break;
            }
            // When a solution must not be built, we must know the "current" package versions
            // to be able to upgrade dependencies in the built projects.
            if( !s.MustBuild )
            {
                var already = s.BuildInfo?.AlreadyBuilt;
                Throw.DebugAssert( "Has been checked in InitializeFromPivot.",
                                    already == null || already.BuildContentInfo != null );
                if( already == null )
                {
                    already = _versionTags.Get( monitor, s.Repo ).FindFirst( s.Solution.GitSolution.GitBranch.Commits, out _ );
                    if( already?.BuildContentInfo == null )
                    {
                        monitor.Error( $"Unable to find a version tag for '{s.Solution.GitSolution}'." );
                        return Task.FromResult( false );
                    }
                }
                foreach( var p in already.BuildContentInfo!.Produced )
                {
                    if( !mapping.TryAdd( p, already.Version ) )
                    {
                        Throw.CKException( "Package identifier conflict." );
                    }
                }
            }
        }
        if( hasDirtyFolder )
        {
            int c = _orderedSolutions.Count( s => s.Repo.GitStatus.IsDirty );
            monitor.Error( c > 1
                            ? $"""
                            Git repositories '{_orderedSolutions.Where( s => s.Repo.GitStatus.IsDirty ).Select( s => s.Repo.DisplayPath.Path ).Concatenate("', '")}' are dirty.
                            Changes must be committed first.
                            """
                            : $"""
                            Git repository '{_orderedSolutions.First( s => s.Repo.GitStatus.IsDirty ).Repo.DisplayPath.Path}' is dirty.
                            Changes must be committed first.
                            """ );
            return Task.FromResult( false );
        }
        var builder = new BuildPlugin.RoadmapBuilder( buildPlugin, context, this, runTest, maxDop, mapping );
        return builder.BuildAsync( monitor );
    }

    public IRenderable ToRenderable( ScreenType screen )
    {
        int buildIndexLen = _buildSolutionCount switch
        {
            0 => 0,
            < 10 => 1,
            < 100 => 2,
            < 1000 => 3,
            _ => 4
        };
        var renderables = ImmutableArray.CreateBuilder<IRenderable>( _orderedSolutions.Length );

        int prevRank = -1;
        for( int i = 0; i < _orderedSolutions.Length; i++ )
        {
            BuildSolution s = _orderedSolutions[i];
            int r = s.Solution.Rank;
            var begOfRank = prevRank < r;
            var endOfRank = i == _orderedSolutions.Length - 1 || _orderedSolutions[i + 1].Solution.Rank > r;

            char cR = begOfRank
                        ? (endOfRank ?  '-' : '╓')
                        : (endOfRank ? '╙' : '║');
            renderables.Add( s.ToRenderable( screen, buildIndexLen, cR ) );
            prevRank = r;
        }
        return new VerticalContent( screen, renderables.MoveToImmutable() );
    }

}
