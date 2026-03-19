using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CSemVer;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{

    public sealed class BuildInfo
    {
        readonly BuildSolution _solution;
        readonly VersionChange _versionChange;
        readonly SVersion _targetVersion;
        readonly ImmutableArray<BuildSolution> _directRequirements;
        readonly MustBuildReason _buildReason;

        readonly Lock _buildTaskLock;
        Task<BuildResult?>? _buildTask;

        public BuildInfo( BuildSolution solution,
                          MustBuildReason buildReason,
                          VersionChange versionChange,
                          SVersion targetVersion,
                          BuildSolution[]? directRequirements )
        {
            _solution = solution;
            _buildReason = buildReason;
            _versionChange = versionChange;
            _targetVersion = targetVersion;
            _directRequirements = directRequirements != null
                                    ? ImmutableCollectionsMarshal.AsImmutableArray( directRequirements )
                                    : [];
            _buildTaskLock = new Lock();
            Throw.DebugAssert( "mustBuild => at least Patch", buildReason == MustBuildReason.None || versionChange >= VersionChange.Patch );
        }

        public BuildSolution Solution => _solution;

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        public bool MustBuild => _buildReason != MustBuildReason.None;

        public MustBuildReason BuildReason => _buildReason;

        public VersionChange VersionChange => _versionChange;

        public SVersion TargetVersion => _targetVersion;

        public ImmutableArray<BuildSolution> DirectRequirements => _directRequirements;

        internal Task<BuildResult?> BuildAsync( BuildPlugin.RoadmapExecutor builder )
        {
            if( _buildTask != null ) return _buildTask;
            lock( _buildTaskLock )
            {
                return _buildTask ??= DoBuildAsync( builder );
            }
        }

        async Task<BuildResult?> DoBuildAsync( BuildPlugin.RoadmapExecutor builder )
        {
            Throw.DebugAssert( MustBuild );
            // Wait for requirements.
            if( _directRequirements.Length > 0 )
            {
                // Checks that all builds went fine (or return null).
                BuildResult?[] req = await Task.WhenAll( _directRequirements.Where( s => s.MustBuild ).Select( s => s.BuildInfo!.BuildAsync( builder ) ).ToArray() );
                foreach( var r in req )
                {
                    if( r == null ) return null;
                }
            }
            // Building requirements succeed: running this build.
            return await builder.BuildAsync( this );
        }

    }

}

