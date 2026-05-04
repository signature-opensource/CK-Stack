using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

public sealed partial class ContentIssueBuilder
{
    sealed class Issue : World.Issue
    {
        readonly List<BranchContentIssueCollector> _branchIssues;

        public Issue( string title,
                      IRenderable body,
                      List<BranchContentIssueCollector> branchIssues )
            : base( title, body, branchIssues[0].Branch.Repo )
        {
            _branchIssues = branchIssues;
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            bool success = true;
            var currentHead = Repo.GitRepository.Repository.Head;
            foreach( var issues in _branchIssues )
            {
                if( issues.Branch.Refresh( monitor ) )
                {
                    // - Creates the "dev/" branch from the hot branch if needed.
                    // - Checks out the "dev/" branch.
                    // - Executes the issues.
                    // - On success, the working folder is committed.
                    if( Repo.GitRepository.Checkout( monitor, issues.Branch.EnsureDevBranch() )
                        && issues.Execute( monitor, context, Repo, Body )
                        && issues.Branch.Commit( monitor, $"""
                    Fixed {_branchIssues.Count} issue(s).

                    {Body.RenderAsString()}
                    """ ) )
                    {
                        continue;
                    }
                    else
                    {
                        success = false;
                    }
                }
                else
                {
                    monitor.Warn( ActivityMonitor.Tags.ToBeInvestigated, $"Branch '{issues.Branch.BranchName.Name}' in '{Repo.DisplayPath}' disappeared. Cannot execute the content issue." );
                }
            }
            // Whatever the success is, it is not up to the "issue -fix" to switch the branch (on, randomly,
            // the most instable one that has an issue...) so we restore the head.
            success &= Repo.GitRepository.Checkout( monitor, currentHead );
            return ValueTask.FromResult( success );
        }
    }
}

