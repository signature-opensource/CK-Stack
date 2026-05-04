using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Centralizes issue generation for the content of a repository: this handles the <see cref="HotBranch"/>
/// content and wraps multiple document related issues into one single Repo issue.
/// </summary>
public sealed partial class ContentIssueBuilder
{
    readonly BranchModelInfo _info;
    readonly Func<IActivityMonitor, ContentIssueEvent, bool> _eventSender;

    internal ContentIssueBuilder( BranchModelInfo info, Func<IActivityMonitor, ContentIssueEvent, bool> eventSender )
    {
        _info = info;
        _eventSender = eventSender;
    }

    internal bool CreateIssue( IActivityMonitor monitor, ScreenType screenType, Action<World.Issue> collector )
    {
        List<BranchContentIssueCollector>? branchIssues = null;
        IRenderable manualBody = screenType.Unit;
        foreach( var b in _info.Branches )
        {
            if( !b.IsActive ) continue;
            BranchContentIssueCollector issues = new BranchContentIssueCollector( b );

            var ev = new ContentIssueEvent( monitor, issues, _info.ShallowSolutionPlugin );
            if( !_eventSender( monitor, ev ) )
            {
                // Stop on the first error.
                return false;
            }
            manualBody = issues.AppendManualDescription( manualBody );
            if( issues.AutoCount > 0 )
            {
                branchIssues ??= new List<BranchContentIssueCollector>();
                branchIssues.Add( issues );
            }
        }
        if( manualBody.Height > 0 )
        {
            collector( World.Issue.CreateManual( "Manual content issues.", manualBody, _info.Repo ) );
        }
        if( branchIssues != null )
        {
            IRenderable body = screenType.Unit;
            foreach( var issues in branchIssues )
            {
                body = issues.AppendBranchDescription( body );
            }
            collector( new Issue( "Content issues.", body, branchIssues ) );
        }
        return true;
    }
}

