using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CKli.BranchModel.Plugin;
using CKli.ArtifactHandler.Plugin;
using System.Threading.Tasks;
using System.Xml.Linq;

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
        if( ev.Content.GetFileInfo( "nuget.config" ) == null )
        {
            var defaultConfig = _artifactHandler.GetDefaultNuGetConfig( ev.Monitor );
            if( defaultConfig != null )
            {
                ev.AddIssue( new CreateNuGetConfigFileIssue( ev.Branch, defaultConfig ) );
            }
        }
    }
}


sealed class CreateNuGetConfigFileIssue : DocumentIssue
{
    readonly XDocument _defaultConfig;

    public CreateNuGetConfigFileIssue( HotBranch branch, XDocument defaultConfig )
        : base( "nuget.config", branch )
    {
        _defaultConfig = defaultConfig;
    }

    protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
    {
        throw new NotImplementedException();
    }
}
