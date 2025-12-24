using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Branch in the <see cref="BranchModelPlugin.BranchNamespace"/>.
/// </summary>
public sealed class BranchName
{
    readonly string _name;
    readonly BranchName? _base;
    readonly BranchName? _devBranch;
    BranchName? _firstChild;
    BranchName? _nextSibling;

    internal BranchName( BranchName? baseBranch, string name, bool dev = false )
    {
        if( baseBranch != null )
        {
            _base = baseBranch;
            if( baseBranch._firstChild == null ) baseBranch._firstChild = this;
            else
            {
                _nextSibling = baseBranch._firstChild;
                baseBranch._firstChild = this;
            }
        }
        _name = name;
        if( !dev )
        {
            _devBranch = new BranchName( this, $"dev/{name}", true );
        }
    }

    /// <summary>
    /// Gets the branch name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the base branch. For a "dev/XXX" branch, this is the regular "XXX" branch.
    /// Null for the "stable" branch.
    /// </summary>
    public BranchName? Base => _base;

    /// <summary>
    /// Gets the associated dev branch.
    /// Null if this is a "dev/XXX" branch.
    /// </summary>
    public BranchName? DevBranch => _devBranch;

    /// <summary>
    /// Gets the regular branch: it is this branch for a "XXX" branch
    /// and the <see cref="Base"/> for a "dev/XXX" branch.
    /// </summary>
    public BranchName RegularBranch => IsDevBranch ? Base : this;

    /// <summary>
    /// Gets whether this branch name has at least one child.
    /// </summary>
    public bool HasChild => _firstChild != null;

    /// <summary>
    /// Gets the children branch names including the <see cref="DevBranch"/> for a regular branch.
    /// A "dev/XXX" branch has no child. 
    /// </summary>
    public IEnumerable<BranchName> Children
    {
        get
        {
            var c = _firstChild;
            while( c != null )
            {
                yield return c;
                c = c._nextSibling;
            }
        } 
    }

    /// <summary>
    /// Gets whether this is a "dev/XXX" branch.
    /// </summary>
    [MemberNotNullWhen( false, nameof( DevBranch ) )]
    [MemberNotNullWhen( true, nameof( Base ) )]
    public bool IsDevBranch => _devBranch == null;

    /// <summary>
    /// Returns the <see cref="Name"/>.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name;

}

