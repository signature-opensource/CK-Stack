using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo
{
    sealed class MissingRootBranchIssue : World.Issue
    {
        readonly BranchInfo _root;
        readonly Branch _starting;

        MissingRootBranchIssue( string title, IRenderable body, BranchInfo root, Branch starting, Repo repo )
            : base( title, body, repo )
        {
            _root = root;
            _starting = starting;
        }

        public static World.Issue Create( IActivityMonitor monitor, BranchInfo root, Branch? starting, ScreenType screenType, Repo repo )
        {
            var title = $"Missing root branch '{root.Expected.Name}'.";
            if( starting == null )
            {
                return CreateManual( title, screenType.Text( "No 'master' nor 'main' branch found." ), repo );
            }
            return new MissingRootBranchIssue( title,
                                               screenType.Text($"Can be fixed by creating it on '{starting.CanonicalName}'."),
                                               root,
                                               starting,
                                               repo );
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            Repo.GitRepository.Repository.CreateBranch( _root.Expected.Name, _starting.Tip );
            return ValueTask.FromResult( true );
        }
    }


}
