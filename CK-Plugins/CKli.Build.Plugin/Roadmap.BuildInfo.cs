using CK.Core;
using CSemVer;
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
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
        readonly bool _mustBuild;
        int _buildIndex;

        public BuildInfo( bool mustBuild,
                          SVersion baseVersion,
                          VersionChange versionChange,
                          SVersion targetVersion,
                          BuildSolution[]? directRequirements )
        {
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
    }
}
