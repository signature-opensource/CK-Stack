using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    public sealed partial class BuildSolution
    {
        readonly Roadmap _roadmap;
        readonly HotGraph.Solution _solution;
        readonly HotGraph.SolutionVersionInfo _versionInfo;
        BuildInfo? _buildInfo;
        HotGraph.SolutionVersionInfo.BuiltVersion _lastBuild;
        BuildContentInfo? _lastBuildToPublish;
        int _buildNumber;
        bool _mustPublish;

        internal BuildSolution( Roadmap roadmap, HotGraph.Solution solution, HotGraph.SolutionVersionInfo versionInfo )
        {
            _roadmap = roadmap;
            _solution = solution;
            _versionInfo = versionInfo;
        }

        #region Initialize
        sealed class PackagesUpdateDetails
        {
            public PackageMapper? WorldRef;
            public PackageMapper? Configuration;
            public PackageMapper? Discrepancies;

            public void Add( (PackageInstance Ref, SVersion To, int MappingIndex) update )
            {
                var m = update.MappingIndex switch
                {
                    0 => WorldRef ??= new PackageMapper(),
                    1 => Configuration ??= new PackageMapper(),
                    _ => Discrepancies ??= new PackageMapper()
                };
                m.Add( update.Ref.PackageId, update.Ref.Version, update.To );
            }
        }

        internal bool Initialize( IActivityMonitor monitor )
        {
            if( _buildInfo != null ) return true;

            MustBuildReason buildReason = MustBuildReason.None;

            // If upstreams are built, always build.
            if( !InitializeUpstreams( monitor,
                                      out BuildSolution[] directRequirements,
                                      out VersionChange vChange,
                                      out bool mustBuildFromUpstreams ) )
            {
                return false;
            }
            if( mustBuildFromUpstreams )
            {
                buildReason |= MustBuildReason.UpstreamBuild;
            }
            // If some packages must be updated, always build.
            // - The alreadyBuiltMapping enables to fix any intra World package references.
            // - The WorldConfiguredMapping applies the VersionTag plugin configuration.
            // - The DiscrepanciesMapping unifies external versions (to the max existing version).
            //
            // The PackagesUpdateDetails collects up to 3 PackageMapper with the package updates for this
            // solution. The display of the roadmap renders them (with the 'U', 'C' and 'D' letters).
            //
            var alreadyBuiltMapping = _roadmap._packageUpdater.GetAlreadyBuiltMapping( _roadmap._isCIBuild );
            var packageUpdates = new PackagesUpdateDetails();
            if( _solution.GitSolution.HasUpdates( packageUpdates.Add,
                                                  mustBuildFromUpstreams ? null : alreadyBuiltMapping,
                                                  _roadmap._packageUpdater.WorldConfiguredMapping,
                                                  _roadmap._packageUpdater.DiscrepanciesMapping ) )
            {
                buildReason |= MustBuildReason.DependencyUpdate;
            }
            Throw.DebugAssert( buildReason == MustBuildReason.None || (buildReason & (MustBuildReason.UpstreamBuild | MustBuildReason.DependencyUpdate)) != 0 );

            // If build is not required here, we check the lastBuild version.
            //
            // When the last build consumes packages in the alreadyBuiltMapping, it means that we are a solution
            // that is impacted by the upstreams but none of our upstreams must be built AND our *.csproj are
            // up to date. This happens when a our upstreams have been built, our *.csproj have been updated but
            // our build failed miserably: the last build tag has not been updated with the upstreams versions.
            // But the last build tag may be a +fake or a +deprecated: we decide to always trigger a build in such
            // cases.

            _lastBuild = _versionInfo.GetLastBuild( _roadmap._isCIBuild );
            if( buildReason == MustBuildReason.None )
            {
                if( _lastBuild.VersionMustBuild )
                {
                    Throw.DebugAssert( _lastBuild.TagCommit.IsFakeVersion || _lastBuild.TagCommit.IsDeprecatedVersion );
                    buildReason |= _lastBuild.TagCommit.IsFakeVersion
                                    ? MustBuildReason.FakeVersion
                                    : MustBuildReason.DeprecatedVersion;
                }
                else
                {
                    Throw.DebugAssert( _lastBuild.TagCommit.BuildContentInfo != null );
                    foreach( var c in _lastBuild.TagCommit.BuildContentInfo.Consumed )
                    {
                        if( alreadyBuiltMapping.TryGetMappedVersion( c.PackageId, c.Version, out var mapped ) && c.Version != mapped )
                        {
                            buildReason |= MustBuildReason.UpstreamVersion;
                            break;
                        }
                    }
                }
            }

            if( buildReason == MustBuildReason.None )
            {
                // Pivot dependent conditions: this build can be skipped if .
                bool canSkip = !_roadmap._isPullBuild && _roadmap._graph.HasPivots && !_solution.IsPivot;
                if( !canSkip )
                {
                    if( _lastBuild.HasCodeChange )
                    {
                        buildReason |= MustBuildReason.CodeChange;
                    }
                }
                if( buildReason == MustBuildReason.None )
                {
                    // We compute the version change not for us (this solution will not be built) but for
                    // the downstream solutions to correctly propagate the change level (here it may be None).
                    vChange = ComputeVersionChange( _versionInfo.BaseBuild.Version, _lastBuild.TagCommit.Version, _lastBuild.TagCommit.IsFakeVersion );
                    _buildInfo = new BuildInfo( this,
                                                MustBuildReason.None,
                                                vChange,
                                                _lastBuild.TagCommit.Version,
                                                directRequirements,
                                                packageUpdates.WorldRef,
                                                packageUpdates.Configuration,
                                                packageUpdates.Discrepancies );
                    return true;
                }
            }
            Throw.DebugAssert( "We must build.", buildReason != MustBuildReason.None );
            // Since we must build, let's update the reason with all its reasons for coherency (and its costs nothing).
            if( _lastBuild.HasCodeChange )
            {
                buildReason |= MustBuildReason.CodeChange;
            }

            // If the upstream doesn't force a Major, we must compute the change from the code in this repository
            // and eventually compute the target version.
            // If we are building from the upstreams or the dependencies must be updated, then we need one more
            // commit to update the dependencies.
            SVersion targetVersion = ComputeTargetVersion( monitor,
                                                           ref vChange,
                                                           mustAddCommit: (buildReason & (MustBuildReason.UpstreamBuild|MustBuildReason.DependencyUpdate)) != 0 );

            _buildInfo = new BuildInfo( this,
                                        buildReason,
                                        vChange,
                                        targetVersion,
                                        directRequirements,
                                        packageUpdates.WorldRef,
                                        packageUpdates.Configuration,
                                        packageUpdates.Discrepancies );
            _roadmap._buildSolutionCount++;
            return true;
        }

        bool InitializeUpstreams( IActivityMonitor monitor,
                                  out BuildSolution[] directRequirements,
                                  out VersionChange maxVersionChange,
                                  out bool mustBuild )
        {
            var solutionRequirements = _solution.DirectRequirements;
            maxVersionChange = VersionChange.None;
            mustBuild = false;
            directRequirements = new BuildSolution[solutionRequirements.Count];
            int idxReq = 0;
            foreach( var req in solutionRequirements )
            {
                var sReq = _roadmap.OrderedSolutions[req.OrderedIndex];
                if( !sReq.Initialize( monitor ) )
                {
                    return false;
                }
                if( sReq.MustBuild )
                {
                    var vReq = sReq.BuildInfo.VersionChange;
                    if( vReq > maxVersionChange )
                    {
                        maxVersionChange = vReq;
                    }
                    mustBuild = true;
                }
                directRequirements[idxReq++] = sReq;
            }
            return true;
        }

        static VersionChange ComputeVersionChange( SVersion vBase, SVersion vTarget, bool targetIsFake )
        {
            Throw.DebugAssert( vBase <= vTarget );
            VersionChange c;
            if( vBase.Major == vTarget.Major )
            {
                if( vBase.Minor == vTarget.Minor )
                {
                    Throw.DebugAssert( "Either we are on the last stable release or a prerelease of the next patch.",
                                        targetIsFake || (vBase == vTarget || vBase.Patch == vTarget.Patch - 1) );
                    c = vBase.Patch == vTarget.Patch
                            ? VersionChange.None
                            :  VersionChange.Patch;
                }
                else
                {
                    Throw.DebugAssert( targetIsFake || (vBase.Minor == vTarget.Minor - 1 && vTarget.Patch == 0) );
                    c = VersionChange.Minor;
                }
            }
            else
            {
                Throw.DebugAssert( targetIsFake || (vBase.Major == vTarget.Major - 1 && vTarget.Minor == 0 && vTarget.Patch == 0) );
                c = VersionChange.Major;
            }
            return c;
        }

        SVersion ComputeTargetVersion( IActivityMonitor monitor,
                                       ref VersionChange vChange,
                                       bool mustAddCommit )
        {
            bool isPrerelease = _roadmap._graph.BranchName.Index != 0;
            SVersion baseVersion = _versionInfo.BaseBuild.Version;
            int prereleaseNumber = -1;
            int prereleaseFixNumber = 0;
            char prereleaseChar = (char)0;
            if( isPrerelease )
            {
                prereleaseChar = _roadmap._graph.BranchName.Name[0];
            }
            if( vChange != VersionChange.Major )
            {
                // Tries to detect a Major change.
                // If on a prerelease branch (not on the root "stable"), we compute the "prerelease and fix number" by
                // keeping the highest among existing versions for this prerelease in the hot commits.
                foreach( var tc in _versionInfo.TagCommitsFromBaseBuild )
                {
                    var existingChange = ComputeVersionChange( _versionInfo.BaseBuild.Version, tc.Version, tc.IsFakeVersion );
                    if( vChange < existingChange )
                    {
                        vChange = existingChange;
                        if( !isPrerelease && vChange == VersionChange.Major )
                        {
                            // If we are handling regular versions, we don't need the commits, we can
                            // stop right now.
                            break;
                        }
                    }
                    // Handling prerelease: compute the "prerelease and fix number".
                    if( isPrerelease )
                    {
                        var prerelease = tc.Version.Prerelease.AsSpan();
                        // If the prerelease string cannot be parsed, this is a warning and we ignore the version.
                        if( prerelease.TryMatch( prereleaseChar )
                            && ParsePreleaseSuffix( monitor, tc.Version, prerelease, out var preNum, out var fixNum, out bool isCI ) )
                        {
                            if( prereleaseNumber < preNum )
                            {
                                prereleaseNumber = preNum;
                                prereleaseFixNumber = fixNum;
                            }
                            else if( prereleaseNumber == preNum )
                            {
                                if( prereleaseFixNumber < fixNum )
                                {
                                    prereleaseFixNumber = fixNum;
                                }
                            }
                        }
                    }
                }

                // If the tag version lookup failed to find a major change, then takes the slow path:
                // consider the commit messages.
                if( vChange != VersionChange.Major )
                {
                    foreach( var c in _versionInfo.CommitsFromBaseBuild )
                    {
                        var detectedChange = DetectVersionChange( c, noNone: true );
                        if( vChange < detectedChange )
                        {
                            vChange = detectedChange;
                            if( vChange == VersionChange.Major )
                            {
                                break;
                            }
                        }
                    }

                    // Ultimately use Patch.
                    if( vChange == VersionChange.None ) vChange = VersionChange.Patch;
                }
            }
            // Ite missa est: we can now compute the target version.
            SVersion? targetVersion;
            if( _roadmap._isCIBuild )
            {
                var d = Repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( _versionInfo.BaseBuild.Commit,
                                                                                                 _versionInfo.GitSolution.GitBranch.Tip );
                Throw.DebugAssert( d.CommonAncestor != null && d.BehindBy is not null );
                int buildNumber = d.BehindBy.Value;
                if( mustAddCommit ) ++buildNumber;

                if( isPrerelease )
                {
                    Throw.DebugAssert( prereleaseChar != '\0' );
                    if( prereleaseNumber == -1 ) prereleaseNumber = 0;
                    string suffix = prereleaseChar + '.' + prereleaseNumber.ToString( CultureInfo.InvariantCulture )
                                    + '.' + prereleaseFixNumber.ToString( CultureInfo.InvariantCulture )
                                    + ".ci." + buildNumber.ToString( CultureInfo.InvariantCulture );
                    targetVersion = NextVersion( vChange, baseVersion, suffix );
                }
                else
                {
                    // Double dash trick here.
                    var ciSuffix = "-ci." + buildNumber.ToString( CultureInfo.InvariantCulture );
                    targetVersion = NextVersion( vChange, baseVersion, ciSuffix );
                }
            }
            else
            {
                if( isPrerelease )
                {
                    Throw.DebugAssert( prereleaseChar != '\0' );
                    string suffix;
                    if( prereleaseNumber == -1 )
                    {
                        suffix = prereleaseChar.ToString();
                    }
                    else
                    {
                        if( vChange <= VersionChange.Patch )
                        {
                            ++prereleaseFixNumber;
                        }
                        else
                        {
                            ++prereleaseNumber;
                            prereleaseFixNumber = 0;
                        }
                        suffix = prereleaseChar + '.' + prereleaseNumber.ToString( System.Globalization.CultureInfo.InvariantCulture );
                        if( prereleaseFixNumber > 0 )
                        {
                            suffix += '.' + prereleaseFixNumber.ToString( System.Globalization.CultureInfo.InvariantCulture );
                        }
                    }
                    targetVersion = NextVersion( vChange, baseVersion, suffix );
                }
                else
                {
                    targetVersion = NextVersion( vChange, baseVersion, null );
                }
            }

            return targetVersion;

            static VersionChange DetectVersionChange( Commit c, bool noNone )
            {
                var message = c.Message;
                // Loosely following the spec here. For us, any appearance of the
                // BREAKING CHANGE anywhere is enough (because of the upper case).
                if( message.Contains( "BREAKING CHANGE", StringComparison.Ordinal )
                    || message.Contains( "BREAKING-CHANGE", StringComparison.Ordinal ) )
                {
                    return VersionChange.Major;
                }

                var m = ConventionalCommitHeader().Match( message );
                if( m.Success )
                {
                    // The ! after the type/scope.
                    if( m.Groups[3].ValueSpan.Length > 0 )
                    {
                        return VersionChange.Major;
                    }
                    var type = m.Groups[1].ValueSpan;
                    return type switch
                    {
                        "feat" => VersionChange.Minor,
                        "merge" or "none" => noNone ? VersionChange.Patch : VersionChange.None,
                        _ => VersionChange.Patch
                    };
                }
                // Consider that merge commits are None.
                return !noNone && c.Parents.Count() > 1
                        ? VersionChange.None
                        : VersionChange.Patch;
            }

            static bool ParsePreleaseSuffix( IActivityMonitor monitor,
                                             SVersion version,
                                             ReadOnlySpan<char> p,
                                             out ushort preNum,
                                             out ushort fixNum,
                                             out bool isCi )
            {
                // A simple "-a" prerelease:
                preNum = 0;
                fixNum = 0;
                isCi = false;
                if( p.Length == 0 )
                {
                    return true;
                }
                // We MUST have a ".NUM" here.
                if( !p.TryMatch( '.' ) || !p.TryMatchInteger( out preNum ) )
                {
                    monitor.Warn( $"Invalid prerelease version pattern '{version}' (expecting '.<number>' suffix for prerelease number), got: '{p}'." );
                    return false;
                }
                // If the text ends here, we are on a numbered prerelease "-a.1" (the "-a.0" doesn't really
                // exist but it corresponds to the simple "-a" prerelease, so we accept this silently).
                if( p.Length == 0 )
                {
                    return true;
                }
                // If there's more, then again we MUST have a ".NUM" here.
                if( !p.TryMatch( '.' ) || !p.TryMatchInteger( out fixNum ) )
                {
                    monitor.Warn( $"Invalid prerelease version pattern '{version}' (expecting '.<number>' suffix for prerelease fix number), got: '{p}'." );
                    return false;
                }
                // If the text ends here, we are on a patch prerelease "-a.1.2" (the "-a.X.0" doesn't really
                // exist but it corresponds to a "-a.X" numbered prerelease, so we accept this silently).
                if( p.Length == 0 )
                {
                    return true;
                }
                // If there's more, then this necessarily is a ".ci.NUM" suffix.
                if( !p.TryMatch( ".ci." ) || !p.TryMatchInteger( out ushort buildNumber ) )
                {
                    monitor.Warn( $"Invalid prerelease version pattern '{version}' (expecting '.ci.<build number>' suffix), got: '{p}'." );
                    return false;
                }
                isCi = true;
                return true;
            }

            static SVersion NextVersion( VersionChange vChange, SVersion baseVersion, string? suffix )
            {
                return vChange switch
                {
                    VersionChange.Major => baseVersion.Major == 0
                                            ? SVersion.Create( 0, baseVersion.Minor + 1, 0, suffix )
                                            : SVersion.Create( baseVersion.Major + 1, 0, 0, suffix ),
                    VersionChange.Minor => SVersion.Create( baseVersion.Major, baseVersion.Minor + 1, 0, suffix ),
                    _ => SVersion.Create( baseVersion.Major, baseVersion.Minor, baseVersion.Patch + 1, suffix )
                };
            }
        }
        #endregion /Initialize

        internal bool ConcludeInitialization( IActivityMonitor monitor,
                                              ReleaseDatabasePlugin releaseDatabase,
                                              ArtifactHandlerPlugin artifactHandlerPlugin,
                                              ref int idxBuildNumber )
        {
            if( MustBuild )
            {
                Throw.DebugAssert( _buildNumber == 0 && idxBuildNumber >= 1 );
                _buildNumber = idxBuildNumber++;
                Throw.DebugAssert( !_mustPublish );

                // The TargetVersion must not already be published.
                var targetVersion = BuildInfo.TargetVersion;
                var published = releaseDatabase.GetBuildContentInfo( monitor, _solution.Repo, targetVersion, fromPublished: true );
                if( published != null )
                {
                    monitor.Error( $"""
                        Repository '{Repo.DisplayPath}' must be build in version '{targetVersion}' but this version already appears in the published database with the content:
                        {published}

                        Local/Remote state seems desynchronized. A 'ckli pull' and/or a 'ckli maintenance release-database rebuild' may be welcome.
                        """ );
                    return false;
                }
                _mustPublish = true;
                ++_roadmap._publishSolutionCount;
            }
            else
            {
                // The CurrentVersion may already be published.
                Throw.DebugAssert( !_mustPublish );
                // If we cannot find the CurrentVersion (that is the last built version) in the Published database then we must (at least)
                // publish it because it may come from a previous build.
                // But, in order to publish it, it must appear in the Local database and its artifacts must be locally available...
                // If not, we must rebuild this version (and eventually publish it).
                // This is an unusual situation as this version should be available somewhere!
                //
                // First idea was to consider that this must be fixed here (and without the "upstream pivot condition"):
                // even if this happens in an upstream of a Pivot, we must trigger the build of this solution.
                //
                // However, this looks more like an issue that can be detected at the VersionTagInfo level, when "ckli issue" is
                // executed (not preemptively), so we error here and ask the user to use "ckli issue". This avoid the "_mustPublish"
                // to appear in the Initialize step and scopes it only here, in the ConcludeInitialization step.
                //
                _mustPublish = releaseDatabase.GetBuildContentInfo( monitor, _solution.Repo, CurrentVersion, fromPublished: true ) == null;
                if( _mustPublish )
                {
                    // The last built version doesn't appear in the Published database but we MUST be able to find it
                    // in the local database because when VersionTagInfo are created, any versioned tag with a parsable content
                    // are automatically inserted or updated in the Local database.
                    _lastBuildToPublish = releaseDatabase.GetBuildContentInfo( monitor, _solution.Repo, CurrentVersion, fromPublished: false );
                    if( _lastBuildToPublish == null )
                    {
                        monitor.Error( ActivityMonitor.Tags.ToBeInvestigated, $"""
                            Repository '{Repo.DisplayPath}' must be published in existing version '{CurrentVersion}' but this version doesn't appear in local release database.
                            """ );
                        return false;
                    }
                    if( !artifactHandlerPlugin.HasAllArtifacts( monitor, _solution.Repo, CurrentVersion, _lastBuildToPublish, out _ ) )
                    {
                        monitor.Error( $"""
                        Repository '{Repo.DisplayPath}' must be published in existing version '{CurrentVersion}' but this version misses local artifacts.
                        Use "ckli issue" for more details.
                        """ );
                        return false;
                    }
                    ++_roadmap._publishSolutionCount;
                }
                Throw.DebugAssert( _buildNumber == 0 );
            }
            return true;
        }

        /// <summary>
        /// Get the roadmap to which this build belongs.
        /// </summary>
        public Roadmap Roadmap => _roadmap;

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _solution.Repo;

        /// <summary>
        /// Gets the solution.
        /// </summary>
        public HotGraph.Solution Solution => _solution;

        /// <summary>
        /// Gets the version related information.
        /// </summary>
        public HotGraph.SolutionVersionInfo VersionInfo => _versionInfo;

        /// <summary>
        /// Gets the base version (the <see cref="VersionTagInfo.HotZoneInfo.LastStable"/> version).
        /// </summary>
        public SVersion BaseVersion => _versionInfo.BaseBuild.Version;

        /// <summary>
        /// Gets the current version (from <see cref="HotGraph.SolutionVersionInfo.GetLastBuild(bool)"/>).
        /// </summary>
        public SVersion CurrentVersion => _lastBuild.TagCommit.Version;

        /// <summary>
        /// Gets the build info. This is null if this solution is not impacted
        /// by any of the <see cref="Roadmap.Pivots"/>.
        /// </summary>
        public BuildInfo? BuildInfo => _buildInfo;

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        [MemberNotNullWhen( true, nameof( BuildInfo ) )]
        public bool MustBuild => _buildInfo != null && _buildInfo.MustBuild;

        /// <summary>
        /// Gets whether this solution should be published:
        /// <see cref="MustBuild"/> is true (the <see cref="BuildInfo.TargetVersion"/> must be published) or the <see cref="CurrentVersion"/>
        /// is not in the published database.
        /// </summary>
        public bool MustPublish => _mustPublish;

        /// <summary>
        /// Gets the version and content that must be published. This MUST be called only if <see cref="MustPublish"/> is
        /// true and after a successful <see cref="Roadmap.BuildAsync(IActivityMonitor, CKliEnv, BuildPlugin, bool?, int)"/>.
        /// </summary>
        /// <returns>The version and content to publish.</returns>
        public (SVersion Version, BuildContentInfo Content) GetFinalPublishInfo()
        {
            Throw.CheckState( MustPublish );
            Throw.CheckState( "A successful build must have been done before.", !MustBuild || BuildInfo.BuildResult != null );

            Throw.DebugAssert( MustBuild || _lastBuildToPublish != null );
            return MustBuild
                    ? (BuildInfo.TargetVersion, BuildInfo.BuildResult!.Content)
                    : (CurrentVersion, _lastBuildToPublish!);
        }

        /// <summary>
        /// Gets the 1-based build number in the order of the <see cref="HotGraph.Solution.OrderedIndex"/>.
        /// 0 when <see cref="MustBuild"/> is false.
        /// </summary>
        public int BuildNumber => _buildNumber;

        internal IRenderable ToRenderable( ScreenType screen, int buildIndexLen, string cRank, ref RStats stats )
        {
            var repo = _solution.Repo.GitRepository;

            IRenderable r = RenderBuildIndexAndRank( screen, buildIndexLen, cRank );

            if( _roadmap.Graph.HasPivots )
            {
                var prefixStyle = new TextStyle( ConsoleColor.Black, ConsoleColor.DarkYellow );
                r = r.AddRight( PivotPrefix( screen, _solution, prefixStyle, marginLeft: 1 ) );
            }

            var statusAndName = RepoName( screen, Repo, MustBuild, BuildInfo == null );
            r = r.AddRight( statusAndName );
            var currentVersion = CurrentVersion.ParsedText!;
            if( MustBuild )
            {
                Throw.DebugAssert( "An error has been emitted if a MustBuild target version has already been published.",
                                   _mustPublish );
                Throw.DebugAssert( BuildInfo.BuildReason != MustBuildReason.None );

                r = r.AddRight( screen.Text( currentVersion, ConsoleColor.Blue ),
                                screen.Text( $"→ v{BuildInfo.TargetVersion} 🡡", ConsoleColor.Green ).Box( marginLeft: 1, marginRight: 1 ),
                                BuildInfo.RenderBuildReason( screen, ref stats ) );
            }
            else
            {
                r = r.AddRight( !_mustPublish
                                    ? screen.Text( currentVersion, ConsoleColor.DarkBlue )
                                    : screen.Text( $"{currentVersion} 🡡", ConsoleColor.Blue ) );
            }
            return r;

            static IRenderable PivotPrefix( ScreenType screen, HotGraph.Solution solution, TextStyle style, int marginLeft )
            {
                if( solution.IsPivot )
                {
                    if( solution.IsPivotDownstream )
                    {
                        if( solution.IsPivotUpstream )
                        {
                            return screen.Text( "→⊙→" ).Box( style, marginLeft: marginLeft );
                        }
                        return screen.Text( "⊙→" ).Box( style, marginLeft: marginLeft, paddingLeft: 1 );
                    }
                    else if( solution.IsPivotUpstream )
                    {
                        return screen.Text( "→⊙" ).Box( style, marginLeft: marginLeft, paddingRight: 1 );
                    }
                    return screen.Text( "⊙" ).Box( style, marginLeft: marginLeft, paddingLeft: 1, paddingRight: 1 );
                }
                else if( solution.IsPivotDownstream )
                {
                    if( solution.IsPivotUpstream )
                    {
                        return screen.Text( "→·→" ).Box( style, marginLeft: marginLeft );
                    }
                    else
                    {
                        return screen.Text( "·→" ).Box( style, marginLeft: marginLeft, paddingLeft: 1 );
                    }
                }
                else if( solution.IsPivotUpstream )
                {
                    return screen.Text( "→·" ).Box( style, marginLeft: marginLeft, paddingRight: 1 );
                }
                return screen.EmptyString.Box( style, marginLeft: marginLeft, marginRight: 3 );
            }

            static IRenderable RepoName( ScreenType screen, Repo repo, bool mustBuild, bool outOfScope )
            {
                var status = repo.GitStatus;
                var style = mustBuild
                                ? new TextStyle( status.IsDirty ? ConsoleColor.Red : ConsoleColor.Green, ConsoleColor.Black )
                                : outOfScope
                                    ? new TextStyle( status.IsDirty ? ConsoleColor.DarkRed : ConsoleColor.DarkGray, ConsoleColor.Black, TextEffect.Strikethrough )
                                    : new TextStyle( status.IsDirty ? ConsoleColor.DarkRed : ConsoleColor.DarkGray, ConsoleColor.Black );
                // First Box.
                IRenderable r = screen.Text( repo.DisplayPath, style ).HyperLink( new Uri( repo.WorkingFolder ) );
                r = status.IsDirty
                            ? r.Box( paddingRight: 1 ).AddLeft( screen.Text( "✱", style.With( TextEffect.Regular ) ).Box( paddingRight: 1 ) )
                            : r.Box( paddingLeft: 2, paddingRight: 1 );
                return r.Box();
            }
        }

        IRenderable RenderBuildIndexAndRank( ScreenType screen, int buildIndexLen, string cRank )
        {
            if( _buildNumber > 0 )
            {
                Throw.DebugAssert( buildIndexLen > 0 );
                var num = _buildNumber.ToString( CultureInfo.InvariantCulture );
                return screen.Text( num.PadRight( buildIndexLen + 1 ) + cRank ).Box( TextStyle.Default );
            }
            var d = screen.Text( cRank );
            return buildIndexLen > 0
                    ? d.Box( TextStyle.Default, paddingLeft: buildIndexLen + 1 )
                    : d.Box( TextStyle.Default );
        }

        [GeneratedRegex( @"^(?<1>\w+)(?:\((?<2>[^()]+)\))?(?<3>!)?:", RegexOptions.CultureInvariant )]
        private static partial Regex ConventionalCommitHeader();

        public override string ToString() => _buildInfo == null
                                                ? $"{_solution} [out of scope]"
                                                : _buildInfo.MustBuild
                                                    ? $"{_solution} [{CurrentVersion} => {_buildInfo.TargetVersion}]"
                                                    : $"{_solution} [no build]";
    }
}
