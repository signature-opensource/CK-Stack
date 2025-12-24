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
        readonly Branch _starting;
        readonly bool _isRootDev;

        MissingRootBranchIssue( string title, IRenderable body, HotBranch root, Branch starting, bool isRootDev, Repo repo )
            : base( title, body, repo )
        {
            _root = root;
            _starting = starting;
            _isRootDev = isRootDev;
        }

        public static World.Issue Create( IActivityMonitor monitor,
                                          HotBranch root,
                                          VersionTagInfo tags,
                                          Branch? startingD,
                                          Branch? startingM,
                                          ScreenType screenType,
                                          Repo repo )
        {
            var title = $"Missing root branch '{root.BranchName.Name}'.";
            bool isRootDev = false;
            Branch start;
            if( startingD == null )
            {
                if( startingM == null )
                {
                    return CreateManual( title, screenType.Text( "No 'master' nor 'main' branch found." ), repo );
                }
                start = startingM;
            }
            else
            {
                isRootDev = true;
                start = startingD;
            }
            return new MissingRootBranchIssue( title,
                                               screenType.Text( $"Can be fixed by creating it from '{start.FriendlyName}'." ),
                                               root,
                                               start,
                                               isRootDev,
                                               repo );
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            Throw.DebugAssert( !_root.BranchName.IsDevBranch );

            var r = Repo.GitRepository.Repository;
            Branch dev;
            if( _isRootDev )
            {
                dev = _starting;
            }
            else
            {
                dev = CreateInitialBranch( r, context.Committer, _starting.Tip, _root.BranchName.DevBranch );
            }
            // Creating the root.
            CreateInitialBranch( r, context.Committer, dev.Tip, _root.BranchName );

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
            var bDev = r.CreateBranch( b.Name, c );
            return bDev;
        }
    }


}
