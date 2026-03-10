using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        BuildInfo? _buildInfo;

        internal BuildSolution( Roadmap roadmap, HotGraph.Solution solution )
        {
            _roadmap = roadmap;
            _solution = solution;
        }

        internal bool InitializeFromPivot( IActivityMonitor monitor, ImmutableArray<VersionTagInfo> allVersionTags )
        {
            // If the _buildInfo has already been computed, we simply return true. We don't have to handle cycles
            // here: they have already been detected by the HotGraph.
            //
            // If we are building without checking upstreams (_roadmap._isPullBuild is false), it doesn't mean that we
            // must ignore the upstreams, it means that we must only consider the upstreams that ARE pivots: the other
            // repositories are ignored, as if no development have been made in them.
            // => This is why we also shortcut, return true (no error) and let the _buildInfo be null if we are building
            //    without upstreams and this repo is not a pivot.
            //    Note that when no Repo are pivots (all of them are), this is like building with upstreams. 
            //
            if( _buildInfo != null || (!_roadmap._isPullBuild && _roadmap._graph.HasPivots && !_solution.IsPivot) )
            {
                return true;
            }

            using var _ = monitor.OpenTrace( $"Computing BuildInfo for '{_solution.Repo.DisplayPath}'." );

            var versionInfo = allVersionTags[Repo.Index];
            Throw.DebugAssert( "This has been checked when initializing Roadmap.", !versionInfo.HasIssue );

            VersionChange vChange = VersionChange.None;
            TagCommit baseCommit = versionInfo.HotZone.LastStable;

            // If we ignore the upstreams, we let the directRequirements be null: if it happens
            // that the build is not required, this data is useless.
            BuildSolution[]? directRequirements = null;
            bool mustBuildFromUpstreams = false;
            if( !InitializeUpstreams( monitor,
                                        finalInitialization: false,
                                        allVersionTags,
                                        out directRequirements,
                                        out vChange,
                                        out mustBuildFromUpstreams ) )
            {
                return false;
            }
            bool mustBuild = mustBuildFromUpstreams;
            var commit = _solution.GitSolution.GitBranch.Tip;
            if( !mustBuild )
            {
                // If this commit has already been built, build is useless.
                var targetCommit = versionInfo.TagCommitsBySha.GetValueOrDefault( commit.Sha );
                if( targetCommit != null )
                {
                    var existingChange = ComputeVersionChange( baseCommit.Version, targetCommit.Version );
                    if( vChange < existingChange )
                    {
                        vChange = existingChange;
                    }
                    // If the targetVersion is not a post-release (with the -- trick) and roadmap.IsCIBuild is true,
                    // this is where we could force a CI build instead of reusing the last version.
                    //
                    // Note that this makes sense only if the branch is bound to "Release build configuration": if the branch
                    // uses "Debug build configuration", generating a CI build here would be useless.
                    //
                    _buildInfo = new BuildInfo( false, baseCommit.Version, vChange, targetCommit.Version, directRequirements );
                    return true;
                }
                mustBuild = true;
            }

            // We must build (from the upstream repositories or because the branch's tip has not been built yet).
            // If the upstream doesn't force a Major, we must compute the change from the code in this repository
            // and eventually compute the target version.
            SVersion targetVersion = ComputeTargetVersion( monitor, versionInfo, ref vChange, baseCommit, commit, mustBuildFromUpstreams );

            // If the upstreams are ignored, because here we must be built, we need to
            // know our requirements to be able to compute our BuildIndex during the
            // second pass.
            if( directRequirements == null )
            {
                var solutionRequirements = _solution.DirectRequirements;
                directRequirements = new BuildSolution[solutionRequirements.Count];
                int idxReq = 0;
                foreach( var req in solutionRequirements )
                {
                    directRequirements[idxReq++] = _roadmap.Solutions[req.Repo.Index];
                }
            }
            _buildInfo = new BuildInfo( true, baseCommit.Version, vChange, targetVersion, directRequirements );
            return true;

        }
        bool InitializeUpstreams( IActivityMonitor monitor,
                                    bool finalInitialization,
                                    ImmutableArray<VersionTagInfo> allVersionTags,
                                    out BuildSolution[]? directRequirements,
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
                var sReq = _roadmap.Solutions[req.Repo.Index];
                if( !(finalInitialization
                        ? sReq.InitializeFinal( monitor, allVersionTags )
                        : sReq.InitializeFromPivot( monitor, allVersionTags )) )
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
                                       VersionTagInfo versionInfo,
                                       ref VersionChange vChange,
                                       TagCommit baseCommit,
                                       Commit commit,
                                       bool mustAddCommit )
        {
            bool isPrerelease = _roadmap._graph.BranchName.Index != 0;
            SVersion baseVersion = baseCommit.Version;
            int prereleaseNumber = -1;
            int prereleaseFixNumber = 0;
            char prereleaseChar = (char)0;
            if( isPrerelease )
            {
                prereleaseChar = _roadmap._graph.BranchName.Name[0];
            }
            if( vChange != VersionChange.Major )
            {
                // Consider all the commits that participate to this code base from the base commit (the LastStable).
                var commitsLog = Repo.GitRepository.Repository.Commits.QueryBy( new CommitFilter()
                {
                    IncludeReachableFrom = commit,
                    ExcludeReachableFrom = baseCommit.Commit,
                    SortBy = CommitSortStrategies.Time | CommitSortStrategies.Reverse
                } );
                // Fast path: join the version tag infos and detect a Major change.
                // Use this first traversal to concretize the list of commits and cache it and 
                // if on a prerelease branch (not on the root "stable"), we compute the "prerelease and fix number" by
                // keeping the highest among existing versions for this prerelease in the hot commits.
                List<Commit> hotCommits = new List<Commit>();
                foreach( var c in commitsLog )
                {
                    hotCommits.Add( c );
                    if( versionInfo.TagCommitsBySha.TryGetValue( c.Sha, out TagCommit? tc ) )
                    {
                        var existingChange = ComputeVersionChange( baseCommit.Version, tc.Version );
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
                }
                // If the tag version lookup failed to find a major change, then takes the slow path:
                // consider the commit messages.
                if( vChange != VersionChange.Major )
                {
                    foreach( var c in hotCommits )
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
                var d = Repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( baseCommit.Commit, commit );
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

        internal bool InitializeFinal( IActivityMonitor monitor, ImmutableArray<VersionTagInfo> allVersionTags )
        {
            // During the first pass (from pivot), either we:
            // - have met the solution and _buildInfo is not null (MustBuild is true or false).
            // - or the solution has not been met and _buildInfo is null.
            //
            if( _buildInfo != null && (!_buildInfo.MustBuild || _buildInfo.BuildIndex != -1) )
            {
                // The solution has been met from the pivots or from this final initialization...
                // and it must not be built or the build index is known: we have nothing to do.
                return true; 
            }
            if( _buildInfo != null )
            {
                // From pivots:
                // We traverse the requirements to obtain the final build index (after the build
                // indices of the requirements).
                Throw.DebugAssert( _buildInfo.MustBuild && _buildInfo.BuildIndex == -1 );
                foreach( var s in _buildInfo.DirectRequirements )
                {
                    if( !s.InitializeFinal( monitor, allVersionTags ) )
                    {
                        return false;
                    }
                }
                _buildInfo.SetBuildIndex( _roadmap._solutionBuildCount++ );
            }
            else
            {
                // This solution has not been impacted by the pivots (because it is not a pivot and doesn't require
                // a pivot or we ignore the upstreams): we analyze the direct requirements and if one of them must be built, then we must
                // also be built (and we initialize the build index).
                if( !InitializeUpstreams( monitor,
                                          finalInitialization: true,
                                          allVersionTags,
                                          out var directRequirements,
                                          out var vChange,
                                          out bool mustBuild ) )
                {
                    return false;
                }
                if( mustBuild )
                {
                    var versionInfo = allVersionTags[Repo.Index];
                    Throw.DebugAssert( "This has been checked when initializing Roadmap.", !versionInfo.HasIssue );

                    TagCommit baseCommit = versionInfo.HotZone.LastStable;
                    SVersion targetVersion = ComputeTargetVersion( monitor, versionInfo, ref vChange, baseCommit, _solution.GitSolution.GitBranch.Tip, true );
                    _buildInfo = new BuildInfo( true, baseCommit.Version, vChange, targetVersion, directRequirements );
                    _buildInfo.SetBuildIndex( _roadmap._solutionBuildCount++ );
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
        /// Gets the build info. This is null if this solution is is not impacted
        /// by any of the <see cref="Roadmap.Pivots"/>.
        /// </summary>
        public BuildInfo? BuildInfo => _buildInfo;

        /// <summary>
        /// Gets whether this solution must be built.
        /// </summary>
        [MemberNotNullWhen( true, nameof( BuildInfo ) )]
        public bool MustBuild => _buildInfo != null && _buildInfo.MustBuild;

        internal IRenderable ToRenderable( ScreenType screen, int buildIndexLen )
        {
            var repo = _solution.Repo.GitRepository;

            // When buildIndexLen, there is nothing to build: skip the numbers totally.
            IRenderable r = buildIndexLen == 0
                            ? screen.Unit
                            : _buildInfo != null && _buildInfo.BuildIndex >= 0
                                ? screen.Text( _buildInfo.BuildIndex.ToString( CultureInfo.InvariantCulture ).PadLeft( buildIndexLen ) + " -" )
                                        .Box( TextStyle.Default )
                                : screen.EmptyString.Box( marginRight: buildIndexLen + 2 );

            if( _roadmap.HasPivots )
            {
                var prefixStyle = new TextStyle( ConsoleColor.Black, ConsoleColor.Yellow );
                if( _buildInfo == null ) prefixStyle = prefixStyle.With( TextEffect.Strikethrough );
                r = r.AddRight( Prefix( screen, _solution, prefixStyle, marginLeft: buildIndexLen == 0 ? 0 : 1 ) );
            }

            var style = _buildInfo == null
                            ? TextStyle.Default.With( TextEffect.Strikethrough )
                            : _buildInfo.MustBuild
                                ? new TextStyle( ConsoleColor.DarkGreen, ConsoleColor.Black )
                                : TextStyle.Default;


            r = r.AddRight( screen.Text( repo.DisplayPath ).HyperLink( new Uri( repo.WorkingFolder ) ).Box( style, paddingLeft: 1, paddingRight: 1 ) );
            if( _buildInfo != null )
            {
                r = r.AddRight( screen.Text( $" ▻ v{_buildInfo.BaseVersion}" ) ).Box( style, paddingRight: 1 );
                if( _buildInfo.MustBuild )
                {
                    r = r.AddRight( screen.Text( $"→ v{_buildInfo.TargetVersion}", new TextStyle( ConsoleColor.Green, ConsoleColor.Black ) ) );
                }
            }
            return r;

            static IRenderable Prefix( ScreenType screen, HotGraph.Solution solution, TextStyle style, int marginLeft )
            {
                if( solution.IsPivot )
                {
                    return screen.Text( "[P]" ).Box( style, marginLeft: marginLeft, paddingLeft: 2, paddingRight: 2 );
                }
                else if( solution.IsPivotDownstream )
                {
                    if( solution.IsPivotUpstream )
                    {
                        return screen.Text( "=>[P]=>" ).Box( style, marginLeft: marginLeft );
                    }
                    else
                    {
                        return screen.Text( "[P]=>" ).Box( style, marginLeft: marginLeft, paddingLeft: 2 );
                    }
                }
                else if( solution.IsPivotUpstream )
                {
                    return screen.Text( "=>[P]" ).Box( style, marginLeft: marginLeft, paddingRight: 2 );
                }
                return screen.EmptyString.Box( style, marginLeft: marginLeft, marginRight: 7 );
            }
        }

        [GeneratedRegex( @"^(?<1>\w+)(?:\((?<2>[^()]+)\))?(?<3>!)?:", RegexOptions.CultureInvariant )]
        private static partial Regex ConventionalCommitHeader();

        public override string ToString() => _buildInfo == null
                                                ? $"{_solution} [out of scope]"
                                                : _buildInfo.MustBuild
                                                    ? $"{_solution} [{_buildInfo.BaseVersion} => {_buildInfo.TargetVersion}]"
                                                    : $"{_solution} [no build]";
    }
}
