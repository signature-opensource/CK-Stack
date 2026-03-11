using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CKli.BranchModel.Plugin.HotGraph;
using static CKli.Build.Plugin.Roadmap;

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
        readonly bool _mustBuild;
        readonly VersionTagInfo _versionInfo;
        int _buildIndex;

        readonly Lock _buildTaskLock;
        Task<BuildResult?>? _build;

        public BuildInfo( BuildSolution solution,
                          bool mustBuild,
                          VersionTag.Plugin.VersionTagInfo versionInfo,
                          SVersion baseVersion,
                          VersionChange versionChange,
                          SVersion targetVersion,
                          BuildSolution[]? directRequirements )
        {
            _solution = solution;
            _mustBuild = mustBuild;
            _versionInfo = versionInfo;
            _buildIndex = -1;
            _baseVersion = baseVersion;
            _versionChange = versionChange;
            _targetVersion = targetVersion;
            _directRequirements = directRequirements != null
                                    ? ImmutableCollectionsMarshal.AsImmutableArray( directRequirements )
                                    : [];
            _buildTaskLock = new Lock();
            Throw.DebugAssert( "mustBuild => at least Patch", !mustBuild || versionChange >= VersionChange.Patch );
        }

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        public bool MustBuild => _mustBuild;

        /// <summary>
        /// Gets the build index. -1 when <see cref="MustBuild"/> is false.
        /// </summary>
        public int BuildIndex => _buildIndex;

        public VersionChange VersionChange => _versionChange;

        public VersionTagInfo VersionInfo => _versionInfo;

        public SVersion BaseVersion => _baseVersion;

        public SVersion TargetVersion => _targetVersion;

        public ImmutableArray<BuildSolution> DirectRequirements => _directRequirements;

        public BuildSolution Solution => _solution;

        internal void SetBuildIndex( int i )
        {
            Throw.DebugAssert( _mustBuild && _buildIndex == -1 );
            _buildIndex = i;
        }
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
            Throw.DebugAssert( _mustBuild );
            // Wait for requirements.
            // Checks that all builds went fine (or return null) and collects the actual produced package instances at the same time.
            BuildResult?[] req = await Task.WhenAll( _directRequirements.Where( s => s.MustBuild ).Select( s => s.BuildInfo!.BuildAsync( builder ) ).ToArray() );
            var allProduced = new Dictionary<string, SVersion>();
            foreach( var r in req )
            {
                if( r == null ) return null;
                foreach( var p in r.Content.Produced )
                {
                    allProduced.Add( p, r.Version );
                }
            }
            // Building requirements succeed: running this build.
            return await builder.BuildAsync( this, BrutalPackageMapper.Create( allProduced ) );
        }

    }

}

