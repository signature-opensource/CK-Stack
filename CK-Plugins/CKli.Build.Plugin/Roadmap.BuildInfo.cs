using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CSemVer;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Detailed build related status associated of a <see cref="BuildSolution"/> in a <see cref="Roadmap"/>.
    /// <para>
    /// Available on <see cref="BuildSolution.BuildInfo"/> when the solution belongs to the pivots scope and is
    /// not ignored. 
    /// </para>
    /// <para>
    /// After a successful build and if <see cref="MustBuild"/> is true, then the non null <see cref="BuildResult"/> is available.
    /// </para>
    /// </summary>
    public sealed class BuildInfo
    {
        readonly BuildSolution _solution;
        readonly MustBuildReason _buildReason;
        readonly VersionChange _versionChange;
        readonly SVersion _targetVersion;
        readonly PackageMapper? _worldReferences;
        readonly PackageMapper? _versionConfigurations;
        readonly PackageMapper? _discrepancies;
        readonly ImmutableArray<BuildSolution> _directRequirements;

        readonly Lock _buildTaskLock;
        Task<BuildResult?>? _buildTask;
        BuildResult? _buildResult;

        internal BuildInfo( BuildSolution solution,
                            MustBuildReason buildReason,
                            VersionChange versionChange,
                            SVersion targetVersion,
                            BuildSolution[]? directRequirements,
                            PackageMapper? worldReferences,
                            PackageMapper? versionConfigurations,
                            PackageMapper? discrepancies )
        {
            _solution = solution;
            _buildReason = buildReason;
            _versionChange = versionChange;
            _targetVersion = targetVersion;
            _worldReferences = worldReferences;
            _versionConfigurations = versionConfigurations;
            _discrepancies = discrepancies;
            _directRequirements = directRequirements != null
                                    ? ImmutableCollectionsMarshal.AsImmutableArray( directRequirements )
                                    : [];
            _buildTaskLock = new Lock();
            Throw.DebugAssert( "mustBuild => at least Patch", buildReason == MustBuildReason.None || versionChange >= VersionChange.Patch );
            Throw.DebugAssert( (worldReferences != null || versionConfigurations != null || discrepancies != null) == ((_buildReason & MustBuildReason.DependencyUpdate) != 0) );
        }

        public BuildSolution Solution => _solution;

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        public bool MustBuild => _buildReason != MustBuildReason.None;

        /// <summary>
        /// Gets why this solution must be built.
        /// </summary>
        public MustBuildReason BuildReason => _buildReason;

        /// <summary>
        /// Gets the version change level.
        /// </summary>
        public VersionChange VersionChange => _versionChange;

        /// <summary>
        /// Gets the version that must be produced.
        /// When <see cref="MustBuild"/> is false, this is the last built version (see <see cref="CKli.BranchModel.Plugin.HotGraph.SolutionVersionInfo.LastBuildInCI"/>).
        /// </summary>
        public SVersion TargetVersion => _targetVersion;

        /// <summary>
        /// Gets the other <see cref="BuildSolution"/> that must be built before this one.
        /// </summary>
        public ImmutableArray<BuildSolution> DirectRequirements => _directRequirements;

        /// <summary>
        /// Gets the intra World reference package updates if any.
        /// </summary>
        public PackageMapper? WorldReferencesUpdates => _worldReferences;

        /// <summary>
        /// Gets the package updates from <see cref="BranchModel.Plugin.HotGraph.PackageUpdater.WorldConfiguredMapping"/> if any.
        /// </summary>
        public PackageMapper? VersionConfigurationUpdates => _versionConfigurations;

        /// <summary>
        /// Gets the package updates from <see cref="BranchModel.Plugin.HotGraph.PackageUpdater.DiscrepanciesMapping"/> if any.
        /// </summary>
        public PackageMapper? DiscrepanciesUpdates => _discrepancies;

        /// <summary>
        /// Gets the build result. Not null when <see cref="MustBuild"/> is true and build succeeded.
        /// </summary>
        public BuildResult? BuildResult => _buildResult;

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
            _buildResult = await builder.BuildAsync( this );
            return _buildResult;
        }


        internal IRenderable RenderBuildReason( ScreenType screen, ref RStats stats )
        {
            IRenderable r = screen.Text( $"({_buildReason})", TextStyle.Default.With( TextEffect.Italic ) );
            if( _worldReferences != null )
            {
                // This is used only when building the upstreams is skipped, the updates here are existing upstreams
                // so we use 'U'.
                r = r.AddBelow( stats.GetUDepHead( screen ).AddRight( screen.Text( _worldReferences.ToString(), ConsoleColor.DarkGray ) ) );
                stats.UDepUpdates += _worldReferences.Count;
            }
            if( _versionConfigurations != null )
            {
                r = r.AddBelow( stats.GetCDepHead( screen ).AddRight( screen.Text( _versionConfigurations.ToString(), ConsoleColor.DarkGray ) ) );
                stats.CDepUpdates += _versionConfigurations.Count;
            }
            if( _discrepancies != null )
            {
                r = r.AddBelow( stats.GetDDepHead( screen ).AddRight( screen.Text( _discrepancies.ToString(), ConsoleColor.DarkGray ) ) );
                stats.DDepUpdates += _discrepancies.Count;
            }
            return r;
        }

    }

}

