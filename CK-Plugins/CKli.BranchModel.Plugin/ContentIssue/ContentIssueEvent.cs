using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Raised by <see cref="BranchModelPlugin"/> on "ckli issue" command when no branch related issues
/// exist: this is used to check the content of the repositories (more precisely, the content of the
/// <see cref="HotBranch"/> that has a <see cref="HotBranch.GitBranch"/>).
/// </summary>
public sealed class ContentIssueEvent : EventMonitoredArgs
{
    readonly HotBranch _branch;
    // We expose NormalizedPath API but use a string with a OrdinalIgnoreCase comparer.
    readonly Dictionary<string, DocumentIssue> _alreadyHandled;

    internal ContentIssueEvent( IActivityMonitor monitor,
                                HotBranch branch,
                                Dictionary<string, DocumentIssue> alreadyHandled )
        : base( monitor )
    {
        Throw.DebugAssert( branch.GitBranch != null );
        _branch = branch;
        _alreadyHandled = alreadyHandled;
    }

    /// <summary>
    /// Gets the hot branch that must be analyzed.
    /// </summary>
    public HotBranch Branch => _branch;

    /// <summary>
    /// Gets the git branch.
    /// </summary>
    public Branch GitBranch => _branch.GitBranch!;

    /// <summary>
    /// Gets whether a document issue has already been generated in a parent connected branch.
    /// </summary>
    /// <param name="path">The document path.</param>
    /// <returns>True on success, false otherwise.</returns>
    public bool HasAlreadyBeenHandled( NormalizedPath path ) => _alreadyHandled.ContainsKey( path );

    /// <summary>
    /// Adds a document issue.
    /// </summary>
    /// <param name="issue">The issue associated to the document.</param>
    public void AddIssue( DocumentIssue issue )
    {
        _alreadyHandled.Add( issue.Path, issue );
    }
}

