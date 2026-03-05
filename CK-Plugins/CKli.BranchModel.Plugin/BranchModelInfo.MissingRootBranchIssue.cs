using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo
{
    sealed class MissingRootBranchIssue : World.Issue
    {
        readonly HotBranch _root;
        readonly Branch _mainOrMaster;

        MissingRootBranchIssue( string title, IRenderable body, HotBranch root, Branch mainOrMaster )
            : base( title, body, root.Repo )
        {
            _root = root;
            _mainOrMaster = mainOrMaster;
        }

        public static World.Issue Create( IActivityMonitor monitor,
                                          HotBranch root,
                                          Branch? mainOrMaster,
                                          ScreenType screenType )
        {
            var title = $"Missing root branch '{root.BranchName.Name}'.";
            if( mainOrMaster == null )
            {
                return CreateManual( title, screenType.Text( $"""
                    No 'master' nor 'main' branch found.
                    The '{root.BranchName.Name}' should be created manually.
                    """ ), root.Repo );
            }
            return new MissingRootBranchIssue( title,
                                               screenType.Text( $"Can be fixed by creating it from '{mainOrMaster.FriendlyName}'." ),
                                               root,
                                               mainOrMaster );
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            BranchLink.CreateAheadBranch( Repo.GitRepository, _mainOrMaster.Tip, _root.BranchName.Name );
            return ValueTask.FromResult( true );
        }
    }


}
