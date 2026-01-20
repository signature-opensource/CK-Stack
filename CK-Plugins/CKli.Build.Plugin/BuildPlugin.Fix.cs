using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Generic;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{

    [Description( """
        Local build the current Fix Workflow.
        """ )]
    [CommandPath( "fix build" )]
    public bool FixBuild( IActivityMonitor monitor,
                          CKliEnv context,
                          [Description( "Don't run tests even if they have never locally run on a commit." )]
                          bool skipTests = false,
                          [Description( "Run tests even if they have already run successfully on a commit." )]
                          bool forceTests = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest )
            || !FixWorkflow.Load( monitor, World, out var exists ) )
        {
            return false;
        }
        if( exists == null )
        {
            monitor.Error( $"No current Fix Workflow exist for world '{World.Name}'." );
            return false;
        }
        if( !_branchModel.CheckBasicPreconditions( monitor, $"building '{exists}'", out var allRepos ) )
        {
            return false;
        }
        return false;
        var updates = new List<NuGetPackageInstance>();
        foreach( var target in exists.Targets )
        {
            using( monitor.OpenInfo( $"Building {target.Index} - {target.Repo.DisplayPath}" ) )
            {
                if( !target.CheckoutBranch( monitor, out int depth ) )
                {
                    return false;
                }

            }

        }
    }
}
