using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    public enum VersionChange
    {
        None,
        Patch,
        Minor,
        Major
    }

    public sealed class BuildInfo
    {
        readonly SVersion _baseVersion;
        readonly VersionChange _versionChange;
        readonly SVersion _targetVersion;
        readonly ImmutableArray<BuildSolution> _directRequirements;
        readonly BuildSolution _solution;
        readonly TagCommit? _alreadyBuilt;
        readonly VersionTagInfo _versionInfo;

        readonly Lock _buildTaskLock;
        Task<BuildResult?>? _build;

        public BuildInfo( BuildSolution solution,
                          TagCommit? alreadyBuilt,
                          VersionTag.Plugin.VersionTagInfo versionInfo,
                          SVersion baseVersion,
                          VersionChange versionChange,
                          SVersion targetVersion,
                          BuildSolution[]? directRequirements )
        {
            _solution = solution;
            _alreadyBuilt = alreadyBuilt;
            _versionInfo = versionInfo;
            _baseVersion = baseVersion;
            _versionChange = versionChange;
            _targetVersion = targetVersion;
            _directRequirements = directRequirements != null
                                    ? ImmutableCollectionsMarshal.AsImmutableArray( directRequirements )
                                    : [];
            _buildTaskLock = new Lock();
            Throw.DebugAssert( "mustBuild => at least Patch", alreadyBuilt != null || versionChange >= VersionChange.Patch );
        }

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        [MemberNotNullWhen( false, nameof( AlreadyBuilt ) )]
        public bool MustBuild => _alreadyBuilt == null;

        public TagCommit? AlreadyBuilt => _alreadyBuilt;

        public VersionChange VersionChange => _versionChange;

        public VersionTagInfo VersionInfo => _versionInfo;

        public SVersion BaseVersion => _baseVersion;

        public SVersion TargetVersion => _targetVersion;

        public ImmutableArray<BuildSolution> DirectRequirements => _directRequirements;

        public BuildSolution Solution => _solution;

        internal Task<BuildResult?> BuildAsync( BuildPlugin.RoadmapBuilder builder )
        {
            if( _build != null ) return _build;
            lock( _buildTaskLock )
            {
                return _build ??= DoBuildAsync( builder );
            }
        }

        async Task<BuildResult?> DoBuildAsync( BuildPlugin.RoadmapBuilder builder )
        {
            Throw.DebugAssert( _alreadyBuilt == null );
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

