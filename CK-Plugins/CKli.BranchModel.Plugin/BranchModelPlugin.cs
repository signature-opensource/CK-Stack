using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed class BranchModelPlugin : RepoPlugin<BranchModelInfo>
{
    readonly BranchTree _branchTree;

    /// <summary>
    /// This is a primary plugin.
    /// </summary>
    public BranchModelPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext.World )
    {
        _branchTree = new BranchTree();
        World.Events.Issue += IssueRequested;
    }

    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        foreach( var r in e.Repos )
        {
            Get( monitor, r ).CollectIssues( monitor, e.ScreenType, e.Add );
        }
    }

    /// <summary>
    /// Gets the branch model.
    /// </summary>
    public BranchTree BranchTree => _branchTree;

    protected override BranchModelInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var info = new BranchModelInfo( repo, _branchTree );
        info.Initialize( monitor );
        return info;
    }


}
