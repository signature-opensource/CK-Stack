using CK.Core;
using CKli.ArtifactHandler.Plugin;
using System.Collections.Immutable;

namespace CKli.Build.Plugin;

/// <summary>
/// Event raised by "ckli fix build" and "ckli fix publish".
/// </summary>
public sealed class FixBuildEventArgs : EventMonitoredArgs
{
    readonly ImmutableArray<BuildResult> _results;
    readonly bool _shouldPublish;

    internal FixBuildEventArgs( IActivityMonitor monitor, ImmutableArray<BuildResult> results, bool shouldPublish )
        : base( monitor )
    {
        _results = results;
        _shouldPublish = shouldPublish;
    }

    /// <summary>
    /// Gets the build results.
    /// </summary>
    public ImmutableArray<BuildResult> Results => _results;

    /// <summary>
    /// Gets whether this build should be published.
    /// </summary>
    public bool ShouldPublish => _shouldPublish;
}
