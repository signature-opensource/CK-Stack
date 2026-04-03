using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
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
    readonly bool _isCIBuild;
    readonly ImmutableArray<BuildSolution> _orderedSolutions;
    readonly ImmutableArray<BuildSolution> _pivots;
    readonly HotGraph.PackageUpdater _packageUpdater;
    readonly Mapping _packageMapping;
    int _buildSolutionCount;

    internal Roadmap( VersionTagPlugin versionTags, HotGraph graph, HotGraph.PackageUpdater packageUpdater, bool isPullBuild, bool isCIBuild )
    {
        _versionTags = versionTags;
        _graph = graph;
        _packageUpdater = packageUpdater;
        _isPullBuild = isPullBuild;
        _isCIBuild = isCIBuild;
        var buildSolutions = new BuildSolution[graph.Solutions.Count];
        var pivots = graph.HasPivots ? new BuildSolution[graph.Pivots.Count] : buildSolutions;
        int iPivot = 0;
        foreach( var s in graph.OrderedSolutions )
        {
            Throw.DebugAssert( "One solution is a pivot => the graph has pivots.", !s.IsPivot || graph.HasPivots );
            Throw.DebugAssert( "PackageUpdater is available => all SolutionVersionInfo are available.", s.VersionInfo != null );
            var sR = new BuildSolution( this, s, s.VersionInfo );
            buildSolutions[s.OrderedIndex] = sR;
            if( s.IsPivot )
            {
                pivots[iPivot++] = sR;
            }
        }
        _orderedSolutions = ImmutableCollectionsMarshal.AsImmutableArray( buildSolutions );
        _packageMapping = new Mapping( packageUpdater, _orderedSolutions, isCIBuild );
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
    /// Gets the <see cref="HotGraph"/> of the world's solutions.
    /// </summary>
    public HotGraph Graph => _graph;

    /// <summary>
    /// Gets the count of <see cref="OrderedSolutions"/> that have true <see cref="BuildSolution.MustBuild"/>.
    /// </summary>
    public int SolutionBuildCount => _buildSolutionCount;

    /// <summary>
    /// Gets whether this is a build on the "dev/" branch (produces CI packages).
    /// </summary>
    public bool IsCIBuild => _isCIBuild;

    /// <summary>
    /// Gets the package mapping.
    /// <para>
    /// Packages produced by the World are either mapped to already built versions or to build target versions and
    /// dependencies external to the World are mapped by <see cref="HotGraph.PackageUpdater.WorldConfiguredMapping"/>
    /// and by <see cref="HotGraph.PackageUpdater.DiscrepanciesMapping"/>.
    /// </para>
    /// </summary>
    public IPackageMapping PackageMapping => _packageMapping;

    internal bool Initialize( IActivityMonitor monitor )
    {
        foreach( var s in _orderedSolutions )
        {
            if( !s.Initialize( monitor ) )
            {
                return false;
            }
        }
        Throw.DebugAssert( _orderedSolutions.Count( s => s.MustBuild ) == _buildSolutionCount );
        if( _buildSolutionCount > 0 )
        {
            int idxNumber = 1;
            foreach( var s in _orderedSolutions )
            {
                if( s.MustBuild ) s.BuildNumber = idxNumber++;
            }
        }
        return true;
    }

    internal Task<BuildResult[]?> BuildAsync( IActivityMonitor monitor, CKliEnv context, BuildPlugin buildPlugin, bool? runTest, int maxDop )
    {
        if( _buildSolutionCount == 0 )
        {
            monitor.Info( ScreenType.CKliScreenTag, "No repositories need to be built." );
            return Task.FromResult( Array.Empty<BuildResult>() )!;
        }

        foreach( var s in _orderedSolutions )
        {
            if( s.Repo.GitStatus.IsDirty )
            {
                int c = _orderedSolutions.Count( s => s.Repo.GitStatus.IsDirty );
                monitor.Error( c > 1
                                ? $"""
                            Git repositories '{_orderedSolutions.Where( s => s.Repo.GitStatus.IsDirty ).Select( s => s.Repo.DisplayPath.Path ).Concatenate( "', '" )}' are dirty.
                            Changes must be committed first.
                            """
                                : $"""
                            Git repository '{_orderedSolutions.First( s => s.Repo.GitStatus.IsDirty ).Repo.DisplayPath.Path}' is dirty.
                            Changes must be committed first.
                            """ );
                return Task.FromResult( (BuildResult[]?)null );
            }
        }
        var builder = new BuildPlugin.RoadmapExecutor( buildPlugin, context, this, runTest, maxDop );
        return builder.BuildAsync( monitor );
    }

    internal struct RStats( int repositoryCount, int buildSolutionCount, bool hasPivots, int pivotsCount, bool isPullBuild, string action )
    {
        IRenderable? _uDepHead;
        IRenderable? _cDepHead;
        IRenderable? _dDepHead;

        public IRenderable GetUDepHead( ScreenType screen ) => _uDepHead ??= DepKind( screen, "U" );
        public IRenderable GetCDepHead( ScreenType screen ) => _cDepHead ??= DepKind( screen, "C" );
        public IRenderable GetDDepHead( ScreenType screen ) => _dDepHead ??= DepKind( screen, "D" );
        public int UDepUpdates;
        public int CDepUpdates;
        public int DDepUpdates;

        public IRenderable Render( ScreenType screen )
        {
            Throw.DebugAssert( (_uDepHead != null) == (UDepUpdates > 0) );
            Throw.DebugAssert( (_cDepHead != null) == (CDepUpdates > 0) );
            Throw.DebugAssert( (_dDepHead != null) == (DDepUpdates > 0) );
            IRenderable r;
            if( buildSolutionCount == 0 )
            {
                r = hasPivots
                    ? screen.Text( $"There is nothing to {action} from the {pivotsCount} pivots out of {repositoryCount} repositories." )
                    : screen.Text( $"There is nothing to {action} across the {repositoryCount} repositories." );
                if( !isPullBuild && hasPivots )
                {
                    r = r.AddBelow( screen.Text( $"(Using '*{action}' may detect required builds in upstreams repositories.)", TextEffect.Italic ) );
                }
                return r;
            }
            r = hasPivots
                    ? screen.Text( $"Required {action} for {buildSolutionCount} from the {pivotsCount} pivots out of {repositoryCount} repositories." )
                    : screen.Text( $"Required {action} for {buildSolutionCount} repositories across the {repositoryCount} repositories." );
            if( _uDepHead == null && _cDepHead == null && _dDepHead == null )
            {
                r = r.AddBelow( screen.Text( $"(No dependency updates other than the ones from the upstreams are needed.)", TextEffect.Italic ) );
            }
            else
            {
                if( _uDepHead != null )
                {
                    r = r.AddBelow( _uDepHead.AddRight( screen.Text( $"{UDepUpdates} updates from upstreams (not using '*{action}' here)." ) ) );
                }
                if( _cDepHead != null )
                {
                    r = r.AddBelow( _cDepHead.AddRight( screen.Text( $"{CDepUpdates} updates from <VersionTag> plugin configuration." ) ) );
                }
                if( _dDepHead != null )
                {
                    r = r.AddBelow( _dDepHead.AddRight( screen.Text( $"{DDepUpdates} updates to fix external dependencies discrepancies." ) ) );
                }
            }
            return r;
        }

        static IRenderable DepKind( ScreenType screen, string kind )
        {
            return screen.Text( kind, foreColor: ConsoleColor.Black, ConsoleColor.DarkMagenta ).Box( marginRight: 1 );
        }

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

        var stats = new RStats( _orderedSolutions.Length, _buildSolutionCount, _graph.HasPivots, _pivots.Length, _isPullBuild, _isCIBuild ? "build" : "publish" );
        var renderables = ImmutableArray.CreateBuilder<IRenderable>( _orderedSolutions.Length );

        int prevRank = -1;
        for( int i = 0; i < _orderedSolutions.Length; i++ )
        {
            BuildSolution s = _orderedSolutions[i];
            int r = s.Solution.Rank;
            var begOfRank = prevRank < r;
            var endOfRank = i == _orderedSolutions.Length - 1 || _orderedSolutions[i + 1].Solution.Rank > r;

            var cR = begOfRank
                        ? (endOfRank ?  "-" : "╓")
                        : (endOfRank ? "╙" : "║");
            renderables.Add( s.ToRenderable( screen, buildIndexLen, cR, ref stats ) );
            prevRank = r;
        }
        return new VerticalContent( screen, renderables.MoveToImmutable() ).TableLayout()
               .AddBelow( stats.Render( screen ) );
    }

}
