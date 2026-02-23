using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo
{
    sealed class MissingRootBranchIssue : World.Issue
    {
        readonly HotBranch _root;
        readonly Branch _mainOrMaster;

        MissingRootBranchIssue( string title, IRenderable body, HotBranch root, Branch mainOrMaster, Repo repo )
            : base( title, body, repo )
        {
            _root = root;
            _mainOrMaster = mainOrMaster;
        }

        public static World.Issue Create( IActivityMonitor monitor,
                                          HotBranch root,
                                          Branch? mainOrMaster,
                                          ScreenType screenType,
                                          Repo repo )
        {
            var title = $"Missing root branch '{root.BranchName.Name}'.";
            if( mainOrMaster == null )
            {
                return CreateManual( title, screenType.Text( $"""
                    No 'master' nor 'main' branch found.
                    The '{root.BranchName.Name}' should be created manually.
                    """ ), repo );
            }
            return new MissingRootBranchIssue( title,
                                               screenType.Text( $"Can be fixed by creating it from '{mainOrMaster.FriendlyName}'." ),
                                               root,
                                               mainOrMaster,
                                               repo );
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            Throw.DebugAssert( !_root.BranchName.IsDevBranch );

            var r = Repo.GitRepository.Repository;
            // Creating the root.
            CreateInitialBranch( r, context.Committer, _mainOrMaster.Tip, _root.BranchName );

            return ValueTask.FromResult( true );
        }

        static Branch CreateInitialBranch( Repository r, Signature committer, Commit fromCommit, BranchName b )
        {
            var c = r.ObjectDatabase.CreateCommit( fromCommit.Author,
                                                   committer,
                                                   $"Initial '{b.Name}'.",
                                                   fromCommit.Tree,
                                                   [fromCommit],
                                                   prettifyMessage: false );
            return r.CreateBranch( b.Name, c );
        }
    }


}
