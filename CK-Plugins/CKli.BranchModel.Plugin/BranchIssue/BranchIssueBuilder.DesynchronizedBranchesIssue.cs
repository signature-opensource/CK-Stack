using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

sealed partial class BranchIssueBuilder
{
    sealed class DesynchronizedBranchesIssue : World.Issue
    {
        readonly List<(Branch Branch, Branch Base)> _desynchronized;

        public DesynchronizedBranchesIssue( IRenderable body, List<(Branch Branch, Branch Base)> desynchronized, Repo repo )
            : base( "Desynchronized branches.", body, repo )
        {
            _desynchronized = desynchronized;
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            bool success = true;
            foreach( var b in _desynchronized )
            {
                success &= (BranchLink.SynchronizeMerge( monitor, Repo.GitRepository, b.Branch, b.Base ) != null);
            }
            return ValueTask.FromResult( success );

        }
    }


}
