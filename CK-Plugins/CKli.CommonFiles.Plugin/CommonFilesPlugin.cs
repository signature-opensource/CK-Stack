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
    }
}
