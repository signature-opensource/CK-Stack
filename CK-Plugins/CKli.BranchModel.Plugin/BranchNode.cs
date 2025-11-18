namespace CKli.BranchModel.Plugin;

/// <summary>
/// Branch in the <see cref="BranchModelPlugin.BranchTree"/>.
/// </summary>
public sealed class BranchNode
{
    readonly string _name;
    readonly BranchNode? _base;

    internal BranchNode()
    {
        _name = "stable";
    }

    internal BranchNode( BranchNode baseBranch, string name )
    {
        _base = baseBranch;
        _name = name;
    }

    /// <summary>
    /// Gets the branch name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the base branch. Null for the "stable" root.
    /// </summary>
    public BranchNode? Base => _base;
}
