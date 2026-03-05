using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    public sealed class BuildSolution
    {
        readonly BuildGroup _group;
        readonly HotGraph.Solution _solution;
        SVersion? _from;
        SVersion? _to;

        internal BuildSolution( BuildGroup g, HotGraph.Solution solution )
        {
            _group = g;
            _solution = solution;
        }

        //internal bool Initialize( IActivityMonitor monitor, VersionTagPlugin versionTag )
        //{
        //    //  We need the version information.
        //    var versionInfo = versionTag.GetWithoutIssue( monitor, _solution.Repo );
        //    if( versionInfo == null )
        //    {
        //        return false;
        //    }
        //    // If the build is required and the branch is not a dev/, the first thing to do is to
        //    // create a dev/ branch and switch to it. 
        //    var vCommit = versionInfo.FindFirst( _solution.GitSolution.Branch.Commits, out _ );
        //    if( vCommit == null )
        //    {
        //        monitor.Error( $"Unable to find base version from '{Repo.DisplayPath}' branch '{_solution.Branch}'." );
        //        return false;
        //    }
            
        //}

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
            var r = _solution.Repo.ToRenderable( screen );
            if( _solution.IsPivot )
            {
                r = r.AddRight( screen.Text( "[P]" ).Box( marginLeft: 1, foreColor: System.ConsoleColor.Gray ) );
            }
            return r;
        }
    }
}
