using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace CKli.ShallowSolution.Plugin;

// Should be HotGraphPlugin?
public sealed class ShallowSolutionPlugin : PrimaryPluginBase
{
    public ShallowSolutionPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
    }

}

