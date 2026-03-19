using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
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
        int _buildNumber;

        internal BuildSolution( Roadmap roadmap, HotGraph.Solution solution, HotGraph.SolutionVersionInfo versionInfo )
        {
            _roadmap = roadmap;
            _solution = solution;
            _versionInfo = versionInfo;
        }

        internal bool InitializeFromPivot( IActivityMonitor monitor )
        {
            // If the _buildInfo has already been computed, we simply return true. We don't have to handle cycles
            // here: they have already been detected by the HotGraph.
            //
            // If we are building without checking upstreams (_roadmap._isPullBuild is false), it doesn't mean that we
            // must ignore the upstreams, it means that we must only consider (in this first step) the upstreams that ARE pivots:
            // the other repositories are ignored, as if they don't exist (as if no development have been made in them).
            // => This is why we also shortcut, return true (no error) and let the _buildInfo be null if we are building
            //    without upstreams and this repo is not a pivot: from the downstream caller point of view, this solution doesn't
            //    need to be built.
            // 
            //    Note that when no Repo are pivots (all of them are), this is like building with upstreams (build == *build). 
            //
            // => This null _buildInfo will be handled during the InitializeFinal() second step.
            //
            if( _buildInfo != null || (!_roadmap._isPullBuild && _roadmap._graph.HasPivots && !_solution.IsPivot) )
            {
                return true;
            }

            using var _ = monitor.OpenTrace( $"Computing BuildInfo for '{_solution.Repo.DisplayPath}'." );

            MustBuildReason buildReason = MustBuildReason.None;
            if( !InitializeUpstreams( monitor,
                                      finalInitialize: false,
                                      out BuildSolution[] directRequirements,
                                      out VersionChange vChange,
                                      out bool mustBuildFromUpstreams ) )
            {
                return false;
            }
            if( mustBuildFromUpstreams )
            {
                buildReason = MustBuildReason.Upstream;
            }
            else if( _versionInfo.VersionMustBuild )
            {
                buildReason = MustBuildReason.Version;
            }
            else if( Roadmap.PackageUpdater.HasUpdates( _solution ) )
            {
                buildReason = MustBuildReason.DependencyUpdate;
            }
            else if( _versionInfo.IsAlreadyBuilt )
            {
                // We compute the version change not for us (this solution will not be built) but for
                // the downstream solutions to correctly propagate the change level (here it may be None).
                vChange = ComputeVersionChange( _versionInfo.BaseBuild.Version, _versionInfo.LastBuild.Version );
                _buildInfo = new BuildInfo( this, MustBuildReason.None, vChange, _versionInfo.LastBuild.Version, directRequirements );
                return true;
            }
            // This handles None => CodeChange for the !IsAlreadyBuilt case.
            if( _versionInfo.HasCodeChange ) buildReason |= MustBuildReason.CodeChange;

            Throw.DebugAssert( "We must build.", buildReason != MustBuildReason.None );
            // If the upstream doesn't force a Major, we must compute the change from the code in this repository
            // and eventually compute the target version.
            SVersion targetVersion = ComputeTargetVersion( monitor,
                                                           ref vChange,
                                                           mustAddCommit: buildReason != MustBuildReason.CodeChange );

            _buildInfo = new BuildInfo( this, buildReason, vChange, targetVersion, directRequirements );
            _roadmap._buildSolutionCount++;
            return true;
        }

        bool InitializeUpstreams( IActivityMonitor monitor,
                                  bool finalInitialize,
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
                if( !(finalInitialize
                        ? sReq.InitializeFinal( monitor )
                        : sReq.InitializeFromPivot( monitor )) )
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

        static VersionChange ComputeVersionChange( SVersion vBase, SVersion vTarget )
        {
            Throw.DebugAssert( vBase <= vTarget );
            VersionChange c;
            if( vBase.Major == vTarget.Major )
            {
                if( vBase.Minor == vTarget.Minor )
                {
                    Throw.DebugAssert( "Either we are on the last stable release or a prerelease of the next patch.",
                                        vBase == vTarget || vBase.Patch == vTarget.Patch - 1 );
                    c = vBase.Patch == vTarget.Patch - 1
                            ? VersionChange.Patch
                            :  VersionChange.None;
                }
                else
                {
                    Throw.DebugAssert( vBase.Minor == vTarget.Minor - 1 && vTarget.Patch == 0 );
                    c = VersionChange.Minor;
                }
            }
            else
            {
                Throw.DebugAssert( vBase.Major == vTarget.Major - 1 && vTarget.Minor == 0 && vTarget.Patch == 0 );
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
                    var existingChange = ComputeVersionChange( _versionInfo.BaseBuild.Version, tc.Version );
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
            if( _roadmap._isDevBuild )
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
                    targetVersion = vChange switch
                    {
                        VersionChange.Major => SVersion.Create( baseVersion.Major + 1, 0, 0, suffix ),
                        VersionChange.Minor => SVersion.Create( baseVersion.Major, baseVersion.Minor + 1, 0, suffix ),
                        _ => SVersion.Create( baseVersion.Major, baseVersion.Minor, baseVersion.Patch + 1, suffix )
                    };
                }
                else
                {
                    // Double dash trick here.
                    var ciSuffix = "-ci." + buildNumber.ToString( CultureInfo.InvariantCulture );
                    targetVersion = vChange switch
                    {
                        VersionChange.Major => SVersion.Create( baseVersion.Major + 1, 0, 0, ciSuffix ),
                        VersionChange.Minor => SVersion.Create( baseVersion.Major, baseVersion.Minor + 1, 0, ciSuffix ),
                        _ => SVersion.Create( baseVersion.Major, baseVersion.Minor, baseVersion.Patch + 1, ciSuffix )
                    };
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
                    targetVersion = vChange switch
                    {
                        VersionChange.Major => SVersion.Create( baseVersion.Major + 1, 0, 0, suffix ),
                        VersionChange.Minor => SVersion.Create( baseVersion.Major, baseVersion.Minor + 1, 0, suffix ),
                        _ => SVersion.Create( baseVersion.Major, baseVersion.Minor, baseVersion.Patch + 1, suffix )
                    };
                }
                else
                {
                    targetVersion = vChange switch
                    {
                        VersionChange.Major => SVersion.Create( baseVersion.Major + 1, 0, 0 ),
                        VersionChange.Minor => SVersion.Create( baseVersion.Major, baseVersion.Minor + 1, 0 ),
                        _ => SVersion.Create( baseVersion.Major, baseVersion.Minor, baseVersion.Patch + 1 )
                    };
                }
            }

            return targetVersion;

            static VersionChange DetectVersionChange( Commit c, bool noNone )
            {
                var message = c.Message;
                // Loosely following the spec here. For use, any appearance of the
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
                // Again, we MUST have a ".NUM" here.
                if( !p.TryMatch( '.' ) || !p.TryMatchInteger( out preNum ) )
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
                // There's more: this necessarily is a ".ci.NUM" suffix.
                if( !p.TryMatch( ".ci." ) || !p.TryMatchInteger( out ushort buildNumber ) )
                {
                    monitor.Warn( $"Invalid prerelease version pattern '{version}' (expecting '.ci.<build number>' suffix), got: '{p}'." );
                    return false;
                }
                isCi = true;
                return true;
            }


        }

        internal bool InitializeFinal( IActivityMonitor monitor )
        {
            // During the first pass (from pivot), either we:
            // - have met the solution and _buildInfo is not null (MustBuild is true or false).
            // - or the solution has not been met and _buildInfo is null.
            //
            if( _buildInfo == null )
            {
                // This solution has not been impacted by the pivots (because it is not a pivot and doesn't require
                // a pivot or we ignore the upstreams): we analyze the direct requirements and if one of them must be built, then we must
                // also be built.
                // We don't build for any other reasons (the LastBuild may be a "+fake" or a "+deprecated"), the package dependencies
                // may require an update or new commits are available): this is the whole point of the "Pivots".
                // They select a "scope" of solutions that must guaranty a coherent set of dependency versions.
                // This is a weak guaranty (especially the "build"): to fully handle a stack, the build must be ran at the root.
                // 
                if( !InitializeUpstreams( monitor,
                                          finalInitialize: true,
                                          out BuildSolution[] directRequirements,
                                          out VersionChange vChange,
                                          out bool mustBuild ) )
                {
                    return false;
                }
                if( mustBuild )
                {
                    SVersion targetVersion = ComputeTargetVersion( monitor, ref vChange, true );
                    _buildInfo = new BuildInfo( this,
                                                MustBuildReason.Upstream,
                                                vChange,
                                                targetVersion,
                                                directRequirements );
                    _roadmap._buildSolutionCount++;
                }
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
        /// Gets the current version (the <see cref="HotGraph.SolutionVersionInfo.LastBuild"/> version).
        /// </summary>
        public SVersion CurrentVersion => _versionInfo.LastBuild.Version;

        /// <summary>
        /// Gets the build info. This is null if this solution is is not impacted
        /// by any of the <see cref="Roadmap.Pivots"/>.
        /// </summary>
        public BuildInfo? BuildInfo => _buildInfo;

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        [MemberNotNullWhen( true, nameof( BuildInfo ) )]
        public bool MustBuild => _buildInfo != null && _buildInfo.MustBuild;

        /// <summary>
        /// Gets the 1-based build number in the order of the <see cref="HotGraph.Solution.OrderedIndex"/>.
        /// 0 when <see cref="MustBuild"/> is false.
        /// </summary>
        public int BuildNumber
        {
            get => _buildNumber;
            internal set => _buildNumber = value;
        }

        internal IRenderable ToRenderable( ScreenType screen, int buildIndexLen, char cRank )
        {
            var repo = _solution.Repo.GitRepository;

            // When buildIndexLen is 0, there is nothing to build: skip the numbers totally.
            IRenderable r = buildIndexLen == 0
                            ? screen.Unit
                            : RenderBuildIndex( screen, buildIndexLen, cRank );

            if( _roadmap.HasPivots )
            {
                var prefixStyle = new TextStyle( ConsoleColor.Black, ConsoleColor.DarkYellow );
                r = r.AddRight( Prefix( screen, _solution, prefixStyle, marginLeft: buildIndexLen == 0 ? 0 : 1 ) );
            }


            var statusAndName = RepoName( screen, Repo, MustBuild, BuildInfo == null );
            r = r.AddRight( statusAndName );
            r = r.AddRight( screen.Text( CurrentVersion.ParsedText! ).Box( foreColor: ConsoleColor.DarkBlue, paddingRight: 1 ) );
            if( MustBuild )
            {
                Throw.DebugAssert( BuildInfo.BuildReason != MustBuildReason.None );
                r = r.AddRight( screen.Text( $"→ v{BuildInfo.TargetVersion}", new TextStyle( ConsoleColor.Green, ConsoleColor.Black ) ),
                                screen.Text( $"({BuildInfo.BuildReason})", TextStyle.Default.With( TextEffect.Italic ) ).Box( paddingLeft: 1 ) );
            }
            return r;

            static IRenderable Prefix( ScreenType screen, HotGraph.Solution solution, TextStyle style, int marginLeft )
            {
                if( solution.IsPivot )
                {
                    return screen.Text( "·" ).Box( style, marginLeft: marginLeft, paddingLeft: 1, paddingRight: 1 );
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
                            ? r.Box( paddingRight: 1 ).AddLeft( screen.Text( "✱" ).Box( paddingRight: 1 ) )
                            : r.Box( paddingLeft: 2, paddingRight: 1 );
                return r;
            }
        }

        IRenderable RenderBuildIndex( ScreenType screen, int buildIndexLen, char cRank )
        {
            if( _buildNumber > 0 )
            {
                var num = _buildNumber.ToString( CultureInfo.InvariantCulture );
                return screen.Text( num.PadRight( buildIndexLen + 1 ) + cRank ).Box( TextStyle.Default );
            }
            return screen.Text( cRank.ToString() ).Box( TextStyle.Default, paddingLeft: buildIndexLen + 1 );
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
