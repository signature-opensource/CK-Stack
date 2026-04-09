using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
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
    readonly bool _mustPublish;
    readonly ImmutableArray<BuildSolution> _orderedSolutions;
    readonly ImmutableArray<BuildSolution> _pivots;
    readonly HotGraph.PackageUpdater _packageUpdater;
    readonly Mapping _packageMapping;
    int _buildSolutionCount;
    int _publishSolutionCount;

    internal Roadmap( VersionTagPlugin versionTags,
                      HotGraph graph,
                      HotGraph.PackageUpdater packageUpdater,
                      bool isPullBuild,
                      bool isCIBuild,
                      bool mustPublish )
    {
        _versionTags = versionTags;
        _graph = graph;
        _packageUpdater = packageUpdater;
        _isPullBuild = isPullBuild;
        _isCIBuild = isCIBuild;
        _mustPublish = mustPublish;
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
    /// Gets the number of solutions that must be published: their <see cref="BuildSolution.MustBuild"/> is true
    /// or the <see cref="BuildSolution.CurrentVersion"/> is not in the published database.
    /// </summary>
    public int SolutionPublishCount => _publishSolutionCount;

    /// <summary>
    /// Gets whether this is a build on the "dev/" branch (produces CI packages).
    /// </summary>
    public bool IsCIBuild => _isCIBuild;

    /// <summary>
    /// Gets whether this roadmap must eventually be published or if the artifacts must be kept locally.
    /// </summary>
    public bool MustPublish => _mustPublish;

    /// <summary>
    /// Gets the package mapping.
    /// <para>
    /// Packages produced by the World are either mapped to already built versions or to build target versions and
    /// dependencies external to the World are mapped by <see cref="HotGraph.PackageUpdater.WorldConfiguredMapping"/>
    /// and by <see cref="HotGraph.PackageUpdater.DiscrepanciesMapping"/>.
    /// </para>
    /// </summary>
    public IPackageMapping PackageMapping => _packageMapping;

    internal bool Initialize( IActivityMonitor monitor, ReleaseDatabasePlugin releaseDatabase, ArtifactHandlerPlugin artifactHandler )
    {
        foreach( var s in _orderedSolutions )
        {
            if( !s.Initialize( monitor ) )
            {
                return false;
            }
        }
        Throw.DebugAssert( _orderedSolutions.Count( s => s.MustBuild ) == _buildSolutionCount );
        bool success = true;
        int idxBuildNumber = 1;
        foreach( var s in _orderedSolutions )
        {
            success &= s.ConcludeInitialization( monitor, releaseDatabase, artifactHandler, ref idxBuildNumber );
        }
        return success;
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

    internal struct RStats( int repositoryCount,
                            int buildSolutionCount,
                            int publishSolutionCount,
                            bool hasPivots,
                            int pivotsCount,
                            bool isPullBuild,
                            bool mustPublish )
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

        readonly string Action => mustPublish ? "publish" : "build";

        public IRenderable Render( ScreenType screen )
        {
            Throw.DebugAssert( (_uDepHead != null) == (UDepUpdates > 0) );
            Throw.DebugAssert( (_cDepHead != null) == (CDepUpdates > 0) );
            Throw.DebugAssert( (_dDepHead != null) == (DDepUpdates > 0) );
            IRenderable r;
            if( buildSolutionCount == 0 )
            {
                r = hasPivots
                    ? screen.Text( $"There is nothing to build from the {pivotsCount} pivots out of {repositoryCount} repositories." )
                    : screen.Text( $"There is nothing to build across the {repositoryCount} repositories." );
                if( !isPullBuild && hasPivots )
                {
                    r = r.AddBelow( screen.Text( $"(Using '*{Action}' may detect required builds in upstreams repositories.)", TextEffect.Italic ) );
                }
            }
            else
            {
                r = hasPivots
                        ? screen.Text( $"Required build for {buildSolutionCount} from the {pivotsCount} pivots out of {repositoryCount} repositories." )
                        : screen.Text( $"Required build for {buildSolutionCount} repositories across the {repositoryCount} repositories." );
                if( _uDepHead == null && _cDepHead == null && _dDepHead == null )
                {
                    r = r.AddBelow( screen.Text( $"(No dependency updates other than the ones from the upstreams are needed.)", TextEffect.Italic ) );
                }
                else
                {
                    if( _uDepHead != null )
                    {
                        r = r.AddBelow( _uDepHead.AddRight( screen.Text( $"{UDepUpdates} updates from upstreams (not using '*{Action}' here)." ) ) );
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
            }
            if( publishSolutionCount == 0 )
            {
                r = r.AddBelow( screen.Text( $"Nothing to publish (the {repositoryCount} repositories are already published)", new TextStyle( ConsoleColor.DarkBlue, ConsoleColor.Black ) ) );
            }
            else
            {
                r = r.AddBelow( screen.Text( $"🡡 {publishSolutionCount} repositories {(mustPublish ? "must" : "can")} be published.", new TextStyle( ConsoleColor.Blue, ConsoleColor.Black ) ) );
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

        var stats = new RStats( _orderedSolutions.Length,
                                _buildSolutionCount,
                                _publishSolutionCount,
                                _graph.HasPivots,
                                _pivots.Length,
                                _isPullBuild,
                                _mustPublish );
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
