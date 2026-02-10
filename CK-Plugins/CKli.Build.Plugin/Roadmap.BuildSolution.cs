using CKli.BranchModel.Plugin;
using CKli.Core;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    public sealed class BuildSolution
    {
        readonly BuildGroup _group;
        readonly HotGraph.Solution _solution;

        internal BuildSolution( BuildGroup g, HotGraph.Solution solution )
        {
            _group = g;
            _solution = solution;
        }

        /// <summary>
        /// Get the group to which this build belongs.
        /// </summary>
        public BuildGroup Group => _group;

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _solution.Repo;


        internal IRenderable ToRenderable( ScreenType screen )
        {
            return _solution.Repo.ToRenderable( screen );
        }

    }
}
