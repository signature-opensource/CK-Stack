using CK.Core;
using CKli.VersionTag.Plugin;

namespace CKli.Build.Plugin;

/// <summary>
/// This event is raised by <see cref="RepoBuilder.BuildAsync(IActivityMonitor, CommitBuildInfo, bool)"/>.
/// It wraps the CommitBuildInfo that describes the build that is about to be ran in
/// the checked out <see cref="CommitBuildInfo.Repo"/>.
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
