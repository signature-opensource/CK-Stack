using System.Xml.Linq;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Reusable pre allocated <see cref="System.Xml.Linq.XName"/>.
/// </summary>
public static class XNames
{
    #pragma warning disable 1591 //Missing XML comment for publicly visible type or member
    public static readonly XName NuGet = XNamespace.None + "NuGet";
    public static readonly XName Feed = XNamespace.None + "Feed";
    public static readonly XName PushQualityFilter = XNamespace.None + "PushQualityFilter";
    public static readonly XName PushCredentials = XNamespace.None + "PushCredentials";
    public static readonly XName FakeReadCredentials = XNamespace.None + "FakeReadCredentials";
    public static readonly XName UserNameKey = XNamespace.None + "UserNameKey";
    public static readonly XName SecretKey = XNamespace.None + "SecretKey";

    internal static XName Name => CKli.Core.XNames.Name;
    internal static XName Url => CKli.Core.XNames.Url;
}
