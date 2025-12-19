using CK.Core;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// "alpha", "beta", "delta", "epsilon", "gamma", "kappa", "pre", "rc" and "stable".
/// </summary>
public sealed class BranchTree
{
    readonly BranchNode _root;
    readonly Dictionary<string, BranchNode> _branches;

    internal BranchTree( string? ltsName )
    {
        _root = new BranchNode( null, "stable" + ltsName );
        _branches = new Dictionary<string, BranchNode>( 10 ) { { _root.Name, _root } };
        var b = _root;
        b = Create( b, "rc" + ltsName );
        b = Create( b, "pre" + ltsName );
        b = Create( b, "kappa" + ltsName );
        b = Create( b, "gamma" + ltsName );
        b = Create( b, "epsilon" + ltsName );
        b = Create( b, "delta" + ltsName );
        b = Create( b, "beta" + ltsName );
        b = Create( b, "alpha" + ltsName );

        BranchNode Create( BranchNode b, string name )
        {
            b = new BranchNode( b, name );
            Throw.DebugAssert( b.DevBranch != null );
            _branches.Add( b.Name, b );
            _branches.Add( b.DevBranch.Name, b.DevBranch );
            return b;
        }
    }

    public BranchNode Root => _root;

    public IReadOnlyDictionary<string, BranchNode> Branches => _branches;


    public BranchNode? Find( string name ) => _branches.GetValueOrDefault( name ); 

}
