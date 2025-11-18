using CK.Core;
using System.Collections.Generic;
using CSemVer;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// "alpha", "beta", "delta", "epsilon", "gamma", "kappa", "preview", "rc", "stable".
/// </summary>
public sealed class BranchTree
{
    readonly BranchNode _root;
    readonly Dictionary<string, BranchNode> _branches;

    internal BranchTree()
    {
        _root = new BranchNode();
        _branches = new Dictionary<string, BranchNode>( 8 ) { { _root.Name, _root } };
        var b = _root;
        for( int i = CSVersion.StandardPrereleaseNames.Count - 1; i >= 0; i-- )
        {
            b = new BranchNode( b, CSVersion.StandardPrereleaseNames[i] );
            _branches.Add( b.Name, b );
        }
    }

    public BranchNode Root => _root;

    public IReadOnlyDictionary<string, BranchNode> Branches => _branches;


    public BranchNode? Find( string name ) => _branches.GetValueOrDefault( name ); 

}
