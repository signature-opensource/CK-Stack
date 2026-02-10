using CKli.BranchModel.Plugin;
using CKli.Core;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Groups of <see cref="Solutions"/> that share the same <see cref="Rank"/>.
    /// </summary>
    public sealed class BuildGroup
    {
        readonly Roadmap _roadmap;
        readonly int _rank;
        readonly ImmutableArray<BuildSolution> _solutions;
 
        internal BuildGroup( Roadmap roadmap, int rank, IEnumerable<HotGraph.Solution> solutions )
        {
            _roadmap = roadmap;
            _rank = rank;
            _solutions = solutions.Select( s => new BuildSolution( this, s ) ).ToImmutableArray();
        }

        /// <summary>
        /// Gets the roadmap to which this group belongs.
        /// </summary>
        public Roadmap Roadmap => _roadmap;

        /// <summary>
        /// Gets this group rank.
        /// </summary>
        public int Rank => _rank;

        /// <summary>
        /// Gets the solutions (ordered by <see cref="Repo.Index"/>).
        /// </summary>
        public ImmutableArray<BuildSolution> Solutions => _solutions;

        internal IRenderable ToRenderable( ScreenType screen )
        {
            return new Collapsable( screen.Text( $"Rank {_rank} ({_solutions.Length})" ).Box()
                                          .AddBelow( _solutions.Select( s => s.ToRenderable( screen ) ) ) );
        }

    }
}
