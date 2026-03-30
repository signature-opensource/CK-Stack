using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using System.Collections.Immutable;

namespace CKli.Build.Plugin;

/// <summary>
/// Event raised by <see cref="BuildPlugin"/> when a <see cref="FixWorkflow"/> has been successfully built.
/// </summary>
public sealed class FixBuildEventArgs : BuildBaseEventArgs
{
    readonly FixWorkflow _fix;
    readonly ImmutableArray<BuildResult> _results;

    internal FixBuildEventArgs( IActivityMonitor monitor, FixWorkflow fix, ImmutableArray<BuildResult> results, bool shouldPublish )
        : base( monitor, shouldPublish )
    {
        _fix = fix;
        _results = results;
    }

    /// <summary>
    /// Gets the fix that has been built.
    /// </summary>
    public FixWorkflow FixWorkflow => _fix;

    /// <summary>
    /// Gets the build results.
    /// </summary>
    public ImmutableArray<BuildResult> Results => _results;

}
