using CK.Core;
using CKli.VersionTag.Plugin;
using CSemVer;

namespace CKli.Build.Plugin;

/// <summary>
/// Wraps a <see cref="CommitBuildInfo"/>.
/// </summary>
public sealed class CoreBuildEventArgs : EventMonitoredArgs
{
    internal CoreBuildEventArgs( IActivityMonitor monitor, CommitBuildInfo buildInfo )
        : base( monitor )
    {
        BuildInfo = buildInfo;
    }

    /// <summary>
    /// Gets the <see cref="CommitBuildInfo"/>.
    /// </summary>
    public CommitBuildInfo BuildInfo { get; }
}
