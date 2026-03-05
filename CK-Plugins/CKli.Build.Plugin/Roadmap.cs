using CK.Core;
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
    readonly Mode _mode;
    readonly ImmutableArray<BuildGroup> _groups;

    internal Roadmap( HotGraph graph, Mode mode )
    {
        _graph = graph;
        _mode = mode;
        _groups = graph.Solutions.GroupBy( s => s.Rank )
                                 .Select( g => new BuildGroup( this, g.Key, g ) )
                                 .ToImmutableArray();
    }

    /// <summary>
    /// Defines how roadmap is computed.
    /// </summary>
    public enum Mode
    {
        /// <summary>
        /// Build existing dev/ branches on pivots, produces local only packages
        /// and propagates them to dev/ branches in downstream repositories.
        /// <para>
        /// This doesn't upgrade the pivots.
        /// </para>
        /// </summary>
        BuildPush,

        /// <summary>
        /// Recursively upgrade the dependencies of the pivots (the upstream repositories), producing
        /// local only packages.
        /// <para>
        /// This doesn't propagate the pivots.
        /// </para>
        /// </summary>
        PullBuild,

        /// <summary>
        /// 
        /// </summary>
        PullBuildPush,
    }

    /// <summary>
    /// Gets the build groups.
    /// </summary>
    public ImmutableArray<BuildGroup> Groups => _groups;

    //internal bool Initialize( IActivityMonitor monitor )
    //{
    //    if( _mode is Mode.PullBuildPush or Mode.PullBuild )
    //    {
    //        foreach( var g in _groups )
    //        {
    //            foreach( var s in g.Solutions )
    //            {
    //                if( s.)
    //            }
    //        }
    //    }
    //}



    public IRenderable ToRenderable( ScreenType screen )
    {
        return screen.Unit.AddBelow( screen.Text( "Roadmap:" ),
                                     _groups.Select( g => g.ToRenderable( screen ) ) );
    }

}
