using CK.Core;
using System;

namespace CKli.Build.Plugin;

/// <summary>
/// Common build event that unifies <see cref="Roadmap.RoadmapBuildEventArgs"/> and <see cref="FixBuildEventArgs"/>.
/// </summary>
public abstract class BuildBaseEventArgs : EventMonitoredArgs
{
    readonly bool _shouldPublish;
    readonly DateTime _buildDate;
    bool _success;

    private protected BuildBaseEventArgs( IActivityMonitor monitor, bool shouldPublish )
        : base( monitor )
    {
        _shouldPublish = shouldPublish;
        _buildDate = DateTime.UtcNow;
        _success = true;
    }

    /// <summary>
    /// Gets whether this build should be published.
    /// </summary>
    public bool ShouldPublish => _shouldPublish;

    /// <summary>
    /// Gets a unique (and centralized) date for this build.
    /// </summary>
    public DateTime BuildDate => _buildDate;

    /// <summary>
    /// Defaults to true: <see cref="SetFailed()"/> sets this to false.
    /// </summary>
    public bool Success => _success;

    /// <summary>
    /// Must be called to signal an error during the event handling (<see cref="Success"/> is true by default).
    /// </summary>
    public void SetFailed() => _success = false;
}
