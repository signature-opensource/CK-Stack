using CK.Core;
using CKli.BranchModel.Plugin;
using System;
using System.Runtime.InteropServices;

namespace CKli.Build.Plugin;


/// <summary>
/// Event raised by <see cref="BuildPlugin"/> when a <see cref="Roadmap"/> has been successfully built.
/// </summary>
public sealed class RoadmapBuildEventArgs : BuildBaseEventArgs
{
    readonly Roadmap _roadmap;

    internal RoadmapBuildEventArgs( IActivityMonitor monitor, Roadmap roadmap, bool shouldPublish )
        : base( monitor, shouldPublish )
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

