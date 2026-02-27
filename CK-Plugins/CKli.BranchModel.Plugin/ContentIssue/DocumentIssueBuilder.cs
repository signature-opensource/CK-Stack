using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Centralizes issue generation for the content of a repository: this handles the <see cref="HotBranch"/>
/// content and wraps multiple document related issues into one single Repo issue.
/// </summary>
sealed class DocumentIssueBuilder
{
    readonly BranchModelInfo _info;
    readonly Func<IActivityMonitor,ContentIssueEvent, bool> _eventSender;

    public DocumentIssueBuilder( BranchModelInfo info, Func<IActivityMonitor,ContentIssueEvent,bool> eventSender )
    {
        _info = info;
        _eventSender = eventSender;
    }

    public bool CreateIssue( IActivityMonitor monitor, out World.Issue? issue )
    {
        // From "stable" to "alpha" (increasing instability index), we consider the branches for which
        // a GitBranch exists.
        // The first disconnected branch stops the propagation: we chose here to strongly acknowledge that
        // a disconnected branch is... disconnected.
        // We then have only a "single set" of document issues per repository.
        var docIssues = new Dictionary<string, DocumentIssue>();
        foreach( var b in _info.Branches )
        {
            if( !b.BranchName.IsConnected ) break;
            // We ignore the dev/ branches here.
            if( b.GitBranch == null || b.BranchName.IsDevBranch ) continue;
            // We are on a non-dev branch that exists.
            // If the associated dev branch exists, we work on it: it replaces
            // the regular branch.
            var dev = _info.Branches[b.BranchName.Index + 1];
            var actualBranch = dev.GitBranch != null ? dev : b;

            var ev = new ContentIssueEvent( monitor, actualBranch, docIssues );
            if( !_eventSender( monitor, ev ) )
            {
                // Stop on the first error.
                break;
            }
        }
        if( docIssues.Count > 0 )
        {
            issue = CreateIssue( monitor, docIssues );
            return issue != null;
        }
        issue = null;
        return true;

        static World.Issue? CreateIssue( IActivityMonitor monitor,  Dictionary<string, DocumentIssue> docIssues  )
        {
            throw new NotImplementedException();
            return null;
        }
    }
}

