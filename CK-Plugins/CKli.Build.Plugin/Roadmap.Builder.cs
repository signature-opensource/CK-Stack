using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    internal sealed class RoadmapBuilder
    {
        readonly Roadmap _roadmap;
        readonly BuildPlugin _buildPlugin;

        public RoadmapBuilder( BuildPlugin buildPlugin, Roadmap roadmap )
        {
            _roadmap = roadmap;
            _buildPlugin = buildPlugin;
        }

        public async Task<bool> StartAsync( IActivityMonitor monitor )
        {
            
            var buildTasks = new Task<bool>[_roadmap.SolutionBuildCount];
            BuildResult?[] req = await Task.WhenAll( _roadmap.Solutions.Where( s => s.MustBuild )
                                                                       .Select( s => s.BuildInfo!.BuildAsync( this ) )
                                                                       .ToArray() );
            return !req.Contains( null );
        }

        internal Task<IActivityMonitor> StartAsync( BuildSolution solution )
        {
            return null!;
        }

        internal void Stop( IActivityMonitor monitor, BuildSolution solution )
        {
        }

        internal bool EnsureAndCheckoutBranch( IActivityMonitor monitor, BuildSolution solution, out bool canAmend )
        {
            var b = solution.Solution.Branch;
            Throw.DebugAssert( b.GitBranch != null );
            Branch workingBranch;
            canAmend = false;
            bool hasDev = b.GitDevBranch != null;
            if( _roadmap.IsDevBuild )
            {
                if( b.GitDevBranch == null )
                {
                    workingBranch = b.EnsureDevBranch();
                    canAmend = true;
                }
                else
                {
                    workingBranch = b.GitDevBranch;
                }
            }
            else
            {
                if( hasDev && !b.IntegrateDevBranch( monitor ) )
                {
                    return false;
                }
                workingBranch = b.GitBranch;
                canAmend = true;
            }
            if( !solution.Repo.GitRepository.Checkout( monitor, workingBranch ) )
            {
                return false;
            }
            return true;
        }

        internal bool UpdateDependenciesAndCommit( IActivityMonitor monitor, BuildSolution solution, IPackageMapping mapping, bool canAmend )
        {
            var s = MutableSolution.Create( monitor, solution.Repo );
            if( s == null )
            {
                return false;
            }
            if( !s.UpdatePackages( monitor, mapping, null ) )
            {
                return false;
            }
            var commitMsg = $"""
                Updating dependencies.

                {mapping.ToString()}
                """;
            return solution.Repo.GitRepository.Commit( monitor,
                                                       commitMsg,
                                                       canAmend
                                                          ? CommitBehavior.AmendIfPossibleAndPrependPreviousMessage
                                                          : CommitBehavior.CreateNewCommit ) != CommitResult.Error;
        }



    }

}
