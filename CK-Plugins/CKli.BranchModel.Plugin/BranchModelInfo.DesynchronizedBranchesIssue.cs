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

        readonly List<HotBranch> _desynchronized;

        public DesynchronizedBranchesIssue( IRenderable body, List<HotBranch> desynchronized, Repo repo )
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
                Throw.DebugAssert( b.IsDesynchronizedBranch
                                   && b.ExistingBaseBranch.GitBranch != null );
                try
                {
                    var result = git.ObjectDatabase.MergeCommits( b.GitBranch.Tip, b.ExistingBaseBranch.GitBranch.Tip, _mergeOptions );
                    var commit = git.ObjectDatabase.CreateCommit( author: context.Committer,
                                                                  committer: context.Committer,
                                                                  message: $"Synchronizing '{b.BranchName}' on '{b.ExistingBaseBranch.BranchName}'.",
                                                                  result.Tree,
                                                                  [b.ExistingBaseBranch.GitBranch.Tip, b.GitBranch.Tip],
                                                                  prettifyMessage: true );
                    git.Refs.UpdateTarget( b.GitBranch.Reference, commit.Id );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"Unable to synchronize branch '{b.BranchName}' on '{b.ExistingBaseBranch.BranchName}'.", ex );
                    success = false;
                }
            }
            return ValueTask.FromResult( success );
        }
    }

}
