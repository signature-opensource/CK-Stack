using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Raised by <see cref="BranchModelPlugin.ContentIssue"/> on "ckli issue" command when no branch related issues
/// exist: this is used to check the content of the repositories (more precisely, the content of the
/// <see cref="HotBranch"/> that has a <see cref="HotBranch.GitBranch"/>).
/// </summary>
public sealed class ContentIssueEvent : EventMonitoredArgs
{
    readonly ShallowSolutionPlugin _shallowSolution;
    readonly BranchContentIssue _branchIssues;
    INormalizedFileProvider? _content;

    internal ContentIssueEvent( IActivityMonitor monitor,
                                BranchContentIssue issues,
                                ShallowSolutionPlugin shallowSolution )
        : base( monitor )
    {
        Throw.DebugAssert( issues.Branch.GitBranch != null );
        _branchIssues = issues;
        _shallowSolution = shallowSolution;
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _branchIssues.Branch.Repo;

    /// <summary>
    /// Gets the hot branch that must be analyzed (<see cref="HotBranch.IsActive"/> is true).
    /// </summary>
    public HotBranch Branch => _branchIssues.Branch;

    /// <summary>
    /// Gets the non null <see cref="HotBranch.GitBranch"/> (because the branch is active).
    /// </summary>
    public Branch GitBranch => _branchIssues.Branch.GitBranch!;

    /// <summary>
    /// Gets the content branch: if it exists, it's the <see cref="HotBranch.GitDevBranch"/> otherwise
    /// the regular <see cref="GitBranch"/> is used.
    /// </summary>
    public Branch GitContentBranch => _branchIssues.Branch.GitDevBranch ?? GitBranch;

    /// <summary>
    /// Gets the content of the <see cref="Branch"/> from <see cref="GitContentBranch"/>.
    /// </summary>
    public INormalizedFileProvider Content => _content ??= _shallowSolution.GetFiles( GitContentBranch.Tip );

    /// <summary>
    /// Gets the issues collector where content issues must be signaled.
    /// </summary>
    public BranchContentIssue Issues => _branchIssues;
}
