using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagInfo
{
    sealed class RemovableVersionTagIssue : World.Issue
    {
        readonly IReadOnlyList<Tag> _tagsToDelete;

        public RemovableVersionTagIssue( string title,
                                         IRenderable body,
                                         Repo repo,
                                         IReadOnlyList<Tag> tagsToDelete )
            : base( title, body, repo )
        {
            _tagsToDelete = tagsToDelete;
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            using var gLog = monitor.OpenInfo( $"Deleting {_tagsToDelete.Count} version tags." );
            foreach( var t in _tagsToDelete )
            {
                Repo.GitRepository.Repository.Tags.Remove( t );
            }
            return ValueTask.FromResult( true );
        }
    }

}

