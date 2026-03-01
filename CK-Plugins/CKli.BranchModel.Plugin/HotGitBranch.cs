using LibGit2Sharp;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Captures a <see cref="HotBranch"/> and its <see cref="HotBranch.GitBranch"/> or its <see cref="HotBranch.GitDevBranch"/>.
/// </summary>
/// <param name="Hot">The hot branch.</param>
/// <param name="GitBranch">The git branch.</param>
public readonly record struct HotGitBranch( HotBranch Hot, Branch GitBranch )
{
    /// <summary>
    /// Gets whether this <see cref="GitBranch"/> is the <see cref="HotBranch.GitDevBranch"/>.
    /// </summary>
    public bool IsDevBranch => GitBranch == Hot.GitDevBranch;

    /// <summary>
    /// Gets whether this <see cref="GitBranch"/> is the <see cref="HotBranch.GitBranch"/>.
    /// </summary>
    public bool IsRegularBranch => GitBranch == Hot.GitBranch;
}
