using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

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
    readonly int _instabilityRank;

    internal BranchName( BranchName? baseBranch, string? ltsName, string name, int instabilityRank )
    {
        _instabilityRank = instabilityRank;
        _base = baseBranch;
        _name = ltsName == null
                    ? name
                    : ltsName + '/' + name;
        if( (instabilityRank & 1) == 0 )
        {
            _devBranch = new BranchName( this, ltsName, $"dev/{name}", instabilityRank + 1 );
        }
    }

    /// <summary>
    /// Gets the branch name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the base branch. For a "dev/XXX" branch, this is never null and always the regular "XXX" branch.
    /// Null for the root "stable" branch or if this branch is disconnected.
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
    /// Gets whether this branch is connected: its <see cref="Base"/> is not null.
    /// <para>
    /// A "dev/" branch is always connected to its <see cref="RegularBranch"/> (that is its <see cref="Base"/>).
    /// The root "stable" branch is never connected (as it is the ultimate base).
    /// </para>
    /// </summary>
    public bool IsConnected => _base != null;

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
    ///     <item><term>...</term><description>(depends on the number of branch names defined)</description></item>
    /// </list>
    /// This is the index in <see cref="BranchNamespace.Branches"/>.
    /// This follows the same pattern as the <see cref="Repo.Index"/>: the <see cref="BranchModelInfo"/> uses this
    /// to associate the corresponding <see cref="HotBranch"/> in each repo.
    /// </summary>
    public int InstabilityRank => _instabilityRank;

    /// <summary>
    /// Gets the branch names from this one up to the stable one (in decreasing <see cref="InstabilityRank"/>)
    /// and stops on the first false <see cref="IsConnected"/> parent.
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

