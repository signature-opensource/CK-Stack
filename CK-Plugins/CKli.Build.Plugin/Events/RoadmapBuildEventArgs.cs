using CK.Core;

namespace CKli.Build.Plugin;


/// <summary>
/// Event raised by <see cref="BuildPlugin"/> when a <see cref="Roadmap"/> has been successfully built.
/// </summary>
public sealed class RoadmapBuildEventArgs : BuildBaseEventArgs
{
    readonly Roadmap _roadmap;

    internal RoadmapBuildEventArgs( IActivityMonitor monitor, Roadmap roadmap )
        : base( monitor, roadmap.MustPublish )
    {
        _roadmap = roadmap;
    }

    /// <summary>
    /// Gets the roadmap that has been successfully build.
    /// The <see cref="BuildInfo.BuildResult">BuildSolution.BuildInfo?.BuildResult</see> is not null for
    /// solutions with a true <see cref="BuildSolution.MustBuild"/>.
    /// </summary>
    public Roadmap Roadmap => _roadmap;
}

