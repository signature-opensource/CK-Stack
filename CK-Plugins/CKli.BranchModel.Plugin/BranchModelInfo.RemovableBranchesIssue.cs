using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;



public sealed partial class BranchModelInfo
{
    internal sealed record RemovableGitBranch( Branch Branch, Branch? Base, string BaseName );

    sealed class RemovableBranchesIssue : World.Issue
    {
        readonly List<RemovableGitBranch> _removables;

        RemovableBranchesIssue( IRenderable body, List<RemovableGitBranch> removables, Repo repo )
            : base( "Removable branches.", body, repo )
        {
            _removables = removables;
        }

        public static RemovableBranchesIssue Create( ScreenType screenType, Repo repo, List<RemovableGitBranch> removables )
        {
            var names = removables.Select( b => b.Base != null
                                                    ? $"- {b.Branch.FriendlyName} is merged into '{b.Base.FriendlyName}'."
                                                    : $"- {b.Branch.FriendlyName} lacks its base '{b.BaseName}' branch." )
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
                bool switchSuccess = true;
                if( git.Head.Tip.Sha == b.Branch.Tip.Sha )
                {
                    monitor.Info( $"Branch to remove is the current head. Switching to its base branch '{b.Base.FriendlyName}'." );
                    success &= Repo.GitRepository.Checkout( monitor, b.Base );
                    switchSuccess = false;
                }
                if( switchSuccess )
                {
                    try
                    {
                        git.Branches.Remove( b.Branch );
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( $"Unable to remove branch '{b.Branch.FriendlyName}'.", ex );
                        success = false;
                    }
                }
            }
            return ValueTask.FromResult( success );
        }
    }

}
