using CK.Core;
using System.Collections.Immutable;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Event raised by <see cref="BranchModelPlugin.FixStartAsync"/>.
/// </summary>
public sealed class FixWorkflowStartEventArgs : EventMonitoredArgs
{
    readonly ImmutableArray<FixWorkflow.TargetRepo> _targets;
    readonly bool _restartingWorkflow;

    internal FixWorkflowStartEventArgs( IActivityMonitor monitor,
                                        ImmutableArray<FixWorkflow.TargetRepo> targets,
                                        bool restartingWorkflow )
        : base( monitor )
    {
        _targets = targets;
        _restartingWorkflow = restartingWorkflow;
    }

    /// <summary>
    /// Gets whether the workflow already exists (current "ckli fix start" restarts it).
    /// </summary>
    public bool RestartingWorkflow => _restartingWorkflow;

    /// <summary>
    /// Gets the ordered fix roadmap.
    /// </summary>
    public ImmutableArray<FixWorkflow.TargetRepo> Targets => _targets;

}

