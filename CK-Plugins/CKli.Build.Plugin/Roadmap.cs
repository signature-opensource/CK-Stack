using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System;
using System.Collections;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using static CKli.BranchModel.Plugin.HotGraph;

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
    readonly ImmutableArray<BuildSolution> _solutions;
    readonly ImmutableArray<BuildSolution> _pivots;
    int _solutionBuildCount;

    internal Roadmap( VersionTagPlugin versionTags, HotGraph graph, bool isPullBuild, bool isCIBuild )
    {
        _versionTags = versionTags;
        _graph = graph;
        _isPullBuild = isPullBuild;
        _isCIBuild = isCIBuild;
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
    /// Gets whether there are pivots: <see cref="Pivots"/> are not the same as <see cref="Solutions"/>.
    /// </summary>
    public bool HasPivots => _graph.HasPivots;

    /// <summary>
    /// Gets the count of <see cref="Solutions"/> that have true <see cref="BuildSolution.MustBuild"/>.
    /// </summary>
    public int SolutionBuildCount => _solutionBuildCount;

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
        foreach( var s in _solutions )
        {
            if( !s.InitializeFinal( monitor, allVersionTags ) )
            {
                return false;
            }
        }
        return true;
    }

    public IRenderable ToRenderable( ScreenType screen )
    {
        return screen.Unit.AddBelow( _solutions.Select( s => s.ToRenderable( screen, Log10BuildIndex() ) ) );
    }

    int Log10BuildIndex() => _solutionBuildCount < 10 ? 1 : _solutionBuildCount < 100 ? 2 : _solutionBuildCount < 1000 ? 3 : 4;

}
