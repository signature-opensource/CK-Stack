using CK.Core;
using CKli.Core;
using System;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    [Description( "Commit any pending changes. Does nothing if there's no change to commit." )]
    [CommandPath( "commit" )]
    public bool Commit( IActivityMonitor monitor,
                        CKliEnv context,
                        [Description( "Required commit message." )]
                        string message,
                        [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                        bool all = false )
    {
        var repos = all
                    ? World.GetAllDefinedRepo( monitor )
                    : World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( repos == null ) return false;
        bool success = true;
        foreach( var repo in repos )
        {
            success &= repo.GitRepository.Commit( monitor, message, CommitBehavior.CreateNewCommit ) != CommitResult.Error;
        }
        return success;
    }

}

