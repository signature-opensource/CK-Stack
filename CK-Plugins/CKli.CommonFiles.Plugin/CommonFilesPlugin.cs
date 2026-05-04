using CK.Core;
using CKli.Core;
using System;
using CKli.BranchModel.Plugin;
using CKli.ArtifactHandler.Plugin;

namespace CKli.CommonFiles.Plugin;

public sealed class CommonFilesPlugin : PrimaryPluginBase
{
    readonly BranchModelPlugin _branchModel;
    readonly ArtifactHandlerPlugin _artifactHandler;

    public CommonFilesPlugin( PrimaryPluginContext primaryContext,
                              BranchModelPlugin branchModel,
                              ArtifactHandlerPlugin artifactHandler )
        : base( primaryContext )
    {
        _branchModel = branchModel;
        _artifactHandler = artifactHandler;
        _branchModel.ContentIssue += ContentIssueRequested;
    }

    void ContentIssueRequested( ContentIssueEvent ev )
    {
        HandleNuGetConfig( ev );
        HandleReadmeFiles( ev );
    }

    void HandleReadmeFiles( ContentIssueEvent ev )
    {
        
    }

    void HandleNuGetConfig( ContentIssueEvent ev )
    {
        NormalizedPath n = "nuget.config";
        var nInfo = ev.Content.GetFileInfo( n );
        if( nInfo == null )
        {
            var defaultConfig = _artifactHandler.GetDefaultNuGetConfig( ev.Monitor );
            if( defaultConfig != null )
            {
                ev.Issues.CreateFile( n, defaultConfig.ToString );
            }
        }
        else if( nInfo.Name != n )
        {
            Throw.DebugAssert( nInfo.Name.Equals( n, StringComparison.OrdinalIgnoreCase ) );
            ev.Issues.MoveFile( nInfo.Name, n );
        }
    }
}
