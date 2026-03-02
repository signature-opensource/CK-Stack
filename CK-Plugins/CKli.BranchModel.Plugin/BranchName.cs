using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Branch in the <see cref="BranchModelPlugin.BranchNamespace"/>.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class BranchName
{
    readonly string _name;
    string? _devName;
    readonly int _index;
    readonly BranchLinkType _linkType;

    internal BranchName( BranchLinkType linkType, string? ltsName, string name, int index )
    {
        _index = index;
        _linkType = linkType;
        _name = ltsName == null
                    ? name
                    : ltsName + '/' + name;
    }

    /// <summary>
    /// Gets the branch name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the "dev/<see cref="Name"/>" branch name.
    /// </summary>
    public string DevName => _devName ??= $"dev/{_name}";

    /// <summary>
    /// Gets the index in <see cref="BranchNamespace.Branches"/>.
    /// This follows the same pattern as the <see cref="Repo.Index"/>: the <see cref="BranchModelInfo"/> uses this
    /// to associate the corresponding <see cref="HotBranch"/> in each repo.
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// Gets the link type.
    /// </summary>
    internal BranchLinkType LinkType => _linkType;

    /// <summary>
    /// Returns the <see cref="Name"/>.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name;

}

