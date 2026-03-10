using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.ShallowSolution.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
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

        internal async Task<BuildResult?> BuildAsync( Builder builder )
        {
            Throw.DebugAssert( _mustBuild );
            BuildResult?[] req = await Task.WhenAll( _directRequirements.Where( s => s.MustBuild ).Select( s => s.BuildInfo!.BuildAsync( builder ) ).ToArray() );
            var allProduced = new Dictionary<string,SVersion>();
            foreach( var r in req )
            {
                if( r == null ) return null;
                foreach( var p in r.Content.Produced )
                {
                    allProduced.Add( p, r.Version );
                }
            }
            var mapper = new AnyMapper( allProduced );

            var monitor = await builder.StartAsync( _solution );

            var b = _solution.Solution.Branch;
            Throw.DebugAssert( b.GitBranch != null );
            Branch workingBranch;
            bool canAmend = false;
            bool hasDev = b.GitDevBranch != null;
            if( builder.IsDevBuild )
            {
                if( b.GitDevBranch == null )
                {
                    workingBranch = b.EnsureDevBranch();
                    canAmend = true;
                }
                else
                {
                    workingBranch = b.GitDevBranch;
                }
            }
            else
            {
                if( hasDev && !b.IntegrateDevBranch( monitor ) )
                {
                    return null;
                }
                workingBranch = b.GitBranch;
                canAmend = true;
            }
            if( !_solution.Repo.GitRepository.Checkout( monitor, workingBranch ) )
            {
                return null;
            }
            var s = MutableSolution.Create( monitor, _solution.Repo );
            if( s == null )
            {
                return null;
            }
            if( !s.UpdatePackages( monitor, mapper  ) )
            {
                return null;
            }
            builder.Stop( monitor, _solution );
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
