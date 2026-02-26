using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CKli.BranchModel.Plugin;

namespace CKli.CommonFiles.Plugin;

public sealed class CommonFilesPlugin : PrimaryPluginBase
{
    readonly BranchModelPlugin _branchModel;

    public CommonFilesPlugin( PrimaryPluginContext primaryContext, BranchModelPlugin branchModel )
        : base( primaryContext )
    {
        _branchModel = branchModel;
    }
}
