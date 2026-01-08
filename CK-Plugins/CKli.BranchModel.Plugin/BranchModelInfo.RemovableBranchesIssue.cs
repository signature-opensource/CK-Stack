using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo
{
    sealed class RemovableBranchesIssue : World.Issue
    {
        readonly List<HotBranch> _removables;

        RemovableBranchesIssue( IRenderable body, List<HotBranch> removables, Repo repo )
            : base( "Removable branches.", body, repo )
        {
            _removables = removables;
        }

        public static RemovableBranchesIssue Create( ScreenType screenType, Repo repo, List<HotBranch> removables )
        {
            var names = removables.Select( b => $"- {b.BranchName.Name} is merged into '{b.ExistingBaseBranch!.BranchName}'." )
                                  .Concatenate( Environment.NewLine );
            var body = screenType.Text( $"""
                                        {names}
                                        {(removables.Count > 1 ? "They" : "It")} can be deleted.
                                        """ );
            return new RemovableBranchesIssue( body, removables, repo );
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            var git = Repo.GitRepository.Repository;
            bool success = true;
            foreach( var b in _removables )
            {
                Throw.DebugAssert( b.GitBranch != null && b.ExistingBaseBranch != null );
                bool switchSuccess = true;
                if( git.Head.Tip.Sha == b.GitBranch.Tip.Sha )
                {
                    var target = b.ExistingBaseBranch.BranchName.Name;
                    monitor.Info( $"Branch to remove is the current head. Switching to its existing base branch '{target}'." );
                    success &= Repo.GitRepository.Checkout( monitor, b.ExistingBaseBranch.BranchName.Name, skipFetchMerge: true );
                    switchSuccess = false;
                }
                if( switchSuccess )
                {
                    try
                    {
                        git.Branches.Remove( b.GitBranch );
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( $"Unable to remove branch '{b.BranchName}'.", ex );
                        success = false;
                    }
                }
            }
            return ValueTask.FromResult( success );
        }
    }

}
