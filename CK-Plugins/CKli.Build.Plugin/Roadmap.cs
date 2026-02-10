using CKli.BranchModel.Plugin;
using CKli.Core;
using System;
using System.Collections;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Build.Plugin;

/// <summary>
/// Augments a <see cref="HotGraph"/> and its <see cref="HotGraph.Solution"/> with versions and build actions.
/// </summary>
public sealed partial class Roadmap
{
    readonly HotGraph _graph;
    readonly ImmutableArray<BuildGroup> _groups;

    internal Roadmap( HotGraph graph )
    {
        _graph = graph;
        _groups = graph.Solutions.GroupBy( s => s.Rank )
                                 .Select( g => new BuildGroup( this, g.Key, g ) )
                                 .ToImmutableArray();
    }

    /// <summary>
    /// Gets the build groups.
    /// </summary>
    public ImmutableArray<BuildGroup> Groups => _groups;

    public IRenderable ToRenderable( ScreenType screen )
    {
        return new VerticalContent( screen, _groups.Select( g => g.ToRenderable( screen ) ).ToImmutableArray() );
    }
}
