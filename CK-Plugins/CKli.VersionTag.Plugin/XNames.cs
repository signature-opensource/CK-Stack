using System.Xml.Linq;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Reusable pre allocated <see cref="System.Xml.Linq.XName"/>.
/// </summary>
public static class XNames
{
    #pragma warning disable 1591 //Missing XML comment for publicly visible type or member
    public static readonly XName MinVersion = XNamespace.None + "MinVersion";
    public static readonly XName MaxVersion = XNamespace.None + "MaxVersion";
}
