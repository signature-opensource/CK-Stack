using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo
{
    sealed class DesynchronizedBranchesIssue : World.Issue
    {
        static readonly MergeTreeOptions _mergeOptions = new MergeTreeOptions() { FailOnConflict = true, SkipReuc = true };

        readonly List<(Branch Branch, Branch Base)> _desynchronized;

        public DesynchronizedBranchesIssue( IRenderable body, List<(Branch Branch, Branch Base)> desynchronized, Repo repo )
            : base( "Desynchronized branches.", body, repo )
        {
            _desynchronized = desynchronized;
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            var git = Repo.GitRepository.Repository;
            bool success = true;
            foreach( var b in _desynchronized )
            {
                try
                {
                    var result = git.ObjectDatabase.MergeCommits( b.Base.Tip, b.Branch.Tip, _mergeOptions );
                    var commit = git.ObjectDatabase.CreateCommit( author: context.Committer,
                                                                  committer: context.Committer,
                                                                  message: $"Synchronizing '{b.Branch.FriendlyName}' on '{b.Base.FriendlyName}'.",
                                                                  result.Tree,
                                                                  [b.Base.Tip, b.Branch.Tip],
                                                                  prettifyMessage: true );
                    git.Refs.UpdateTarget( b.Branch.Reference, commit.Id );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"Unable to synchronize branch '{b.Branch.FriendlyName}' on '{b.Base.FriendlyName}'.", ex );
                    success = false;
                }
            }
            return ValueTask.FromResult( success );
        }
    }

}
