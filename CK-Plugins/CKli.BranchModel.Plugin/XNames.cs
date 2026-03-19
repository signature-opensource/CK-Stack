using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Reusable pre allocated <see cref="System.Xml.Linq.XName"/>.
/// </summary>
public static class XNames
{
    #pragma warning disable 1591 //Missing XML comment for publicly visible type or member
    public static readonly XName Version = XNamespace.None + "Version";
    public static XName Name => CKli.Core.XNames.Name;
}
