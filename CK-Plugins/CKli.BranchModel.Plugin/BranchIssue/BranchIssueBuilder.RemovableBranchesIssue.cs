using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

sealed partial class BranchIssueBuilder
{
    sealed class RemovableBranchesIssue : World.Issue
    {
        readonly List<(Branch Branch, object BaseOrName)> _removables;

        RemovableBranchesIssue( IRenderable body, List<(Branch Branch, object BaseOrName)> removables, Repo repo )
            : base( "Removable branches.", body, repo )
        {
            _removables = removables;
        }

        public static RemovableBranchesIssue Create( ScreenType screenType, Repo repo, List<(Branch Branch, object BaseOrName)> removables )
        {
            var names = removables.Select( r => r.BaseOrName switch
                                                {
                                                    string name => $"- '{r.Branch.FriendlyName}' lacks its base '{name}' branch.",
                                                    Branch b => $"- '{r.Branch.FriendlyName}' is merged into '{b.FriendlyName}'.",
                                                    _ => Throw.NotSupportedException<string>()
                                                } )
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
            var git = Repo.GitRepository;
            bool success = true;
            foreach( var r in _removables )
            {
                bool localSuccess = true;
                if( r.Branch.IsCurrentRepositoryHead )
                {
                    if( r.BaseOrName is Branch branchBase )
                    {
                        monitor.Info( $"Branch to remove is the current head. Switching to its base branch '{branchBase.FriendlyName}'." );
                        if( !git.Checkout( monitor, branchBase ) )
                        {
                            success = false;
                            localSuccess = false;
                        }
                    }
                }
                if( localSuccess )
                {
                    success &= git.DeleteBranch( monitor, r.Branch, DeleteGitBranchMode.WithTrackedBranch );
                }
            }
            return ValueTask.FromResult( success );
        }
    }

}
