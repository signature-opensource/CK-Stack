using CK.Core;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Event raised by <see cref="BuildPlugin"/>.
    /// </summary>
    public sealed class BuildEventArgs : EventMonitoredArgs
    {
        readonly Roadmap _roadmap;
        readonly bool _shouldPublish;

        internal BuildEventArgs( IActivityMonitor monitor, Roadmap roadmap, bool shouldPublish )
            : base( monitor )
        {
            _roadmap = roadmap;
            _shouldPublish = shouldPublish;
        }

        /// <summary>
        /// Gets the roadmap that has been successfully build.
        /// </summary>
        public Roadmap Roadmap => _roadmap;

        /// <summary>
        /// Gets whether this build should be published.
        /// </summary>
        public bool ShouldPublish => _shouldPublish;
    }

}
