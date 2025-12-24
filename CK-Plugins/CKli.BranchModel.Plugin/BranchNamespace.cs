using CK.Core;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// "alpha", "beta", "delta", "epsilon", "gamma", "kappa", "pre", "rc" and "stable".
/// </summary>
public sealed class BranchNamespace
{
    readonly BranchName _root;
    readonly Dictionary<string, BranchName> _branches;

    internal BranchNamespace( string? ltsName )
    {
        _root = new BranchName( null, "stable" + ltsName );
        Throw.DebugAssert( _root.DevBranch != null );
        _branches = new Dictionary<string, BranchName>( 18 ) { { _root.Name, _root }, { _root.DevBranch.Name, _root.DevBranch } };
        var b = _root;
        b = Create( b, "rc" + ltsName );
        b = Create( b, "pre" + ltsName );
        b = Create( b, "kappa" + ltsName );
        b = Create( b, "gamma" + ltsName );
        b = Create( b, "epsilon" + ltsName );
        b = Create( b, "delta" + ltsName );
        b = Create( b, "beta" + ltsName );
        b = Create( b, "alpha" + ltsName );

        BranchName Create( BranchName b, string name )
        {
            b = new BranchName( b, name );
            Throw.DebugAssert( b.DevBranch != null );
            _branches.Add( b.Name, b );
            _branches.Add( b.DevBranch.Name, b.DevBranch );
            return b;
        }
    }

    /// <summary>
    /// Gets the "stable" root branch name.
    /// </summary>
    public BranchName Root => _root;

    /// <summary>
    /// Gets an index of the branch names by their <see cref="BranchName.Name"/>.
    /// </summary>
    public IReadOnlyDictionary<string, BranchName> Branches => _branches;


    /// <summary>
    /// Finds a branch name.
    /// </summary>
    /// <param name="name">The branch name.</param>
    /// <returns>The branch or null.</returns>
    public BranchName? Find( string name ) => _branches.GetValueOrDefault( name ); 

}
