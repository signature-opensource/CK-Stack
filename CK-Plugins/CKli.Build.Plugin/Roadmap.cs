using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System;
using System.Collections;
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
    readonly bool _isDevBuild;
    readonly ImmutableArray<BuildSolution> _solutions;
    readonly ImmutableArray<BuildSolution> _pivots;
    ImmutableArray<BuildSolution> _orderedBuildSolutions;

    internal Roadmap( VersionTagPlugin versionTags, HotGraph graph, bool isPullBuild, bool isDevBuild )
    {
        _versionTags = versionTags;
        _graph = graph;
        _isPullBuild = isPullBuild;
        _isDevBuild = isDevBuild;
        var buildSolutions = new BuildSolution[graph.Solutions.Count];
        var pivots = graph.HasPivots ? new BuildSolution[graph.Pivots.Count] : buildSolutions;
        int iPivot = 0;
        foreach( var s in graph.Solutions )
        {
            Throw.DebugAssert( "one solution is a pivot => the graph has pivots.", !s.IsPivot || graph.HasPivots );
            var sR = new BuildSolution( this, s );
            buildSolutions[s.Repo.Index] = sR;
            if( s.IsPivot )
            {
                pivots[iPivot++] = sR;
            }
        }
        _solutions = ImmutableCollectionsMarshal.AsImmutableArray( buildSolutions );
        _pivots = ImmutableCollectionsMarshal.AsImmutableArray( pivots );
    }

    /// <summary>
    /// Gets the build solutions (ordered by <see cref="Repo.Index"/>).
    /// </summary>
    public ImmutableArray<BuildSolution> Solutions => _solutions;

    /// <summary>
    /// Gets the pivots solutions if some has been specified.
    /// This is never empty (no pivot means all solutions are pivot).
    /// <para>
    /// This list is ordered by <see cref="Repo.Index"/>.
    /// </para>
    /// </summary>
    public ImmutableArray<BuildSolution> Pivots => _pivots;

    /// <summary>
    /// Gets the final solutions that must be built ordered by <see cref="BuildInfo.BuildIndex"/>.
    /// </summary>
    public ImmutableArray<BuildSolution> OrderedBuildSolutions => _orderedBuildSolutions;

    /// <summary>
    /// Gets whether there are pivots: <see cref="Pivots"/> are not the same as <see cref="Solutions"/>.
    /// </summary>
    public bool HasPivots => _graph.HasPivots;

    /// <summary>
    /// Gets the count of <see cref="Solutions"/> that have true <see cref="BuildSolution.MustBuild"/>.
    /// </summary>
    public int SolutionBuildCount => _orderedBuildSolutions.Length;

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
        var finalBuildOrderedSolutions = ImmutableArray.CreateBuilder<BuildSolution>();
        foreach( var s in _solutions )
        {
            if( !s.InitializeFinal( monitor, allVersionTags, finalBuildOrderedSolutions ) )
            {
                return false;
            }
        }
        _orderedBuildSolutions = finalBuildOrderedSolutions.DrainToImmutable();
        return true;
    }

    internal Task<bool> BuildAsync( IActivityMonitor monitor, CKliEnv context, BuildPlugin buildPlugin, bool? runTest, int maxDop )
    {
        if( _orderedBuildSolutions.Length == 0 )
        {
            monitor.Info( ScreenType.CKliScreenTag, "No repositories need to be built." );
            return Task.FromResult( true );
        }
        var builder = new BuildPlugin.RoadmapBuilder( buildPlugin, context, this, runTest, maxDop );
        return builder.BuildAsync( monitor );
    }

    public IRenderable ToRenderable( ScreenType screen )
    {
        return screen.Unit.AddBelow( _orderedBuildSolutions.Select( s => s.ToRenderable( screen, Log10BuildIndex() ) ) );


    }

    int Log10BuildIndex() => _orderedBuildSolutions.Length switch
                            {
                                0 => 0,
                                < 10 => 1,
                                < 100 => 2,
                                < 1000 => 3,
                                _ => 4
                            };

}
