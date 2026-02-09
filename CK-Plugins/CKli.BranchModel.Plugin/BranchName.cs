using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Branch in the <see cref="BranchModelPlugin.BranchNamespace"/>.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class BranchName
{
    readonly string _name;
    readonly BranchName? _base;
    readonly BranchName? _devBranch;
    BranchName? _firstChild;
    BranchName? _nextSibling;
    readonly int _instabilityRank;

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
            _instabilityRank = baseBranch._instabilityRank + (dev ? 1 : 2);
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
    /// Gets a value that ranks this branch name in terms of the "instability":
    /// this starts with 0 for "stable", this is odd for "dev/" branches and even for regular names:
    /// <list type="bullet">
    ///     <item><term>0</term><description>"stable"</description></item>
    ///     <item><term>1</term><description>"dev/stable"</description></item>
    ///     <item><term>2</term><description>"rc"</description></item>
    ///     <item><term>3</term><description>"dev/rc"</description></item>
    ///     <item><term>4</term><description>"pre"</description></item>
    ///     <item><term>...</term><description>(depends on the number of branch names defined)</description></item>
    /// </list>
    /// </summary>
    public int InstabilityRank => _instabilityRank;

    /// <summary>
    /// Gets the branch names from this one up to the stable one (in decreasing <see cref="InstabilityRank"/>).
    /// <para>
    /// When <see cref="IsDevBranch"/> is true, this returns the interleaved base dev/ branches.
    /// For instance, fallbacks of "dev/pre" branch are:
    /// <code>dev/pre, pre, dev/rc, rc, dev/stable, stable</code>
    /// For the regular branch "pre", this is:  
    /// <code>pre, rc, stable</code>
    /// </para>
    /// </summary>
    public IEnumerable<BranchName> Fallbacks
    {
        get
        {
            return _devBranch != null ? RegularFallbacks( this ) : DevFallbacks( this );

            static IEnumerable<BranchName> DevFallbacks( BranchName from )
            {
                var b = from;
                do
                {
                    Throw.DebugAssert( "We are on a dev/XXX branch.", b._base != null );
                    yield return b; // dev/XXX
                    b = b._base;
                    yield return b; // XXX
                    b = b._base;
                }
                while( b != null );
            }

            static IEnumerable<BranchName> RegularFallbacks( BranchName from )
            {
                var b = from;
                do
                {
                    yield return b;
                    b = b._base;
                }
                while( b != null );
            }
        }
    }

    /// <summary>
    /// Returns the <see cref="Name"/>.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name;

}

