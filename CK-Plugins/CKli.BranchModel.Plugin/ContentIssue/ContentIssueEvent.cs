using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Raised by <see cref="BranchModelPlugin.ContentIssue"/> on "ckli issue" command when no branch related issues
/// exist: this is used to check the content of the repositories (more precisely, the content of the
/// <see cref="HotBranch"/> that has a <see cref="HotBranch.GitBranch"/>).
/// </summary>
public sealed class ContentIssueEvent : EventMonitoredArgs
{
    readonly Repo _repo;
    readonly ShallowSolutionPlugin _shallowSolution;
    HotBranch? _branch;
    INormalizedFileProvider? _content;


    internal ContentIssueEvent( IActivityMonitor monitor,
                                Repo repo,
                                ShallowSolutionPlugin shallowSolution )
        : base( monitor )
    {
        _repo = repo;
        _shallowSolution = shallowSolution;
    }

    internal void Initialize( HotBranch branch )
    {
        Throw.DebugAssert( branch.GitBranch != null );
        _branch = branch;
        _content = null;
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the hot branch that must be analyzed (<see cref="HotBranch.IsActive"/> is true).
    /// </summary>
    public HotBranch Branch => _branch!;

    /// <summary>
    /// Gets the non null <see cref="HotBranch.GitBranch"/> (because the branch is active).
    /// </summary>
    public Branch GitBranch => _branch!.GitBranch!;

    /// <summary>
    /// Gets the content branch: if it exists, it's the <see cref="HotBranch.GitDevBranch"/> otherwise
    /// the regular <see cref="HotBranch.GitBranch"/> is used.
    /// </summary>
    public Branch GitContentBranch => _branch!.GitDevBranch ?? _branch!.GitBranch!;

    /// <summary>
    /// Gets the content of the <see cref="Branch"/> from <see cref="GitContentBranch"/>.
    /// </summary>
    public INormalizedFileProvider Content => _content ??= _shallowSolution.GetFiles( GitContentBranch.Tip );



}

