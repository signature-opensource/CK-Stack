using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
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
        int _buildIndex;

        readonly Lock _buildLock;
        Task<BuildResult?>? _build;

        public BuildInfo( BuildSolution solution,
                          bool mustBuild,
                          SVersion baseVersion,
                          VersionChange versionChange,
                          SVersion targetVersion,
                          BuildSolution[]? directRequirements )
        {
            _solution = solution;
            _mustBuild = mustBuild;
            _buildIndex = -1;
            _baseVersion = baseVersion;
            _versionChange = versionChange;
            _targetVersion = targetVersion;
            _directRequirements = directRequirements != null
                                    ? ImmutableCollectionsMarshal.AsImmutableArray( directRequirements )
                                    : [];
            _buildLock = new Lock();
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

        public SVersion BaseVersion => _baseVersion;

        public SVersion TargetVersion => _targetVersion;

        public ImmutableArray<BuildSolution> DirectRequirements => _directRequirements;

        internal void SetBuildIndex( int i )
        {
            Throw.DebugAssert( _mustBuild && _buildIndex == -1 );
            _buildIndex = i;
        }
        internal Task<BuildResult?> BuildAsync( RoadmapBuilder builder )
        {
            if( _build != null ) return _build;
            lock( _buildLock )
            {
                return _build ??= DoBuildAsync( builder );
            }
        }

        async Task<BuildResult?> DoBuildAsync( RoadmapBuilder builder )
        {
            Throw.DebugAssert( _mustBuild );
            // Wait for requirements.
            // Check that all builds went fine and collect the actual produced package instances at the same time.
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
            // Building requirements succeed: enter this build.
            var mapper = new AnyMapper( allProduced );

            // Starts this build by acquiring a monitor.
            var monitor = await builder.StartAsync( _solution );

            if( !builder.EnsureAndCheckoutBranch( monitor, _solution, out bool canAmend ) )
            {
                return null;
            }
            if( !builder.UpdateDependenciesAndCommit( monitor, _solution, mapper, canAmend ) )
            {
                return null;
            }

            builder.Stop( monitor, _solution );
            throw new NotImplementedException();
        }

        sealed class AnyMapper : IPackageMapping
        {
            readonly Dictionary<string, SVersion> _mapper;

            public AnyMapper( Dictionary<string, SVersion> mapper ) => _mapper = mapper;

            public bool IsEmpty => _mapper.Count == 0;

            public bool HasMapping( string packageId ) => _mapper.ContainsKey( packageId );

            public SVersion? GetMappedVersion( string packageId, SVersion from ) => _mapper.GetValueOrDefault( packageId );
        }
    }

}
