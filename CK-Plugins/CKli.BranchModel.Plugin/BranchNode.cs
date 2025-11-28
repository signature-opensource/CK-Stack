using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Branch in the <see cref="BranchModelPlugin.BranchTree"/>.
/// </summary>
public sealed class BranchNode
{
    readonly string _name;
    readonly BranchNode? _base;
    readonly BranchNode? _devBranch;

    internal BranchNode( BranchNode? baseBranch, string name, bool dev = false )
    {
        _base = baseBranch;
        _name = name;
        if( !dev )
        {
            _devBranch = new BranchNode( this, $"{name}-dev", true );
        }
    }

    /// <summary>
    /// Gets the branch name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the base branch. Null for the "stable" root.
    /// </summary>
    public BranchNode? Base => _base;

    /// <summary>
    /// Gets the dev branch if this is "stable", "rc", "pre", "beta", "alpha",
    /// null if this is a "XXX-dev" branch.
    /// </summary>
    public BranchNode? DevBranch => _devBranch;

    /// <summary>
    /// Gets whether this is a not a "XXX-dev" branch.
    /// </summary>
    [MemberNotNullWhen( false, nameof( DevBranch ) )]
    public bool IsDevBranch => _devBranch == null;

}

