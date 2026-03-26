using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Event raised by <see cref="BuildPlugin"/>.
    /// </summary>
    public sealed class BuildEventArgs : EventMonitoredArgs
    {
        readonly Roadmap _roadmap;
        readonly DateTime _buildDate;
        readonly bool _shouldPublish;

        internal BuildEventArgs( IActivityMonitor monitor, Roadmap roadmap, bool shouldPublish )
            : base( monitor )
        {
            _roadmap = roadmap;
            _shouldPublish = shouldPublish;
            _buildDate = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets the roadmap that has been successfully build.
        /// The <see cref="BuildInfo.BuildResult">BuildSolution.BuildInfo?.BuildResult</see> is not null for
        /// solutions with a true <see cref="BuildSolution.MustBuild"/>.
        /// </summary>
        public Roadmap Roadmap => _roadmap;

        /// <summary>
        /// Gets whether this build should be published.
        /// </summary>
        public bool ShouldPublish => _shouldPublish;

        /// <summary>
        /// Gets a unique (and centralized) date for this build.
        /// </summary>
        public DateTime BuildDate => _buildDate;
    }

}
