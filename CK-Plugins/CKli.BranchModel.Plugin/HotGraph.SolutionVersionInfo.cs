using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System.Collections.Generic;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class HotGraph
{
    /// <summary>
    /// Captures version related information for a <see cref="Solution"/>.
    /// Exposed by <see cref="Solution.VersionInfo"/> but initialized by a successful call to <see cref="HotGraph.GetPackageUpdater(IActivityMonitor)"/>.
    /// Requires that <see cref="VersionTagPlugin"/> has no issue.
    /// </summary>
    public sealed class SolutionVersionInfo
    {
        readonly Solution _solution;
        readonly VersionTagInfo _info;
        readonly string _builtTipSha;
        readonly TagCommit _lastAnyBuild;
        readonly IReadOnlyList<Commit> _commitsFromBaseBuild;
        readonly IReadOnlyList<TagCommit> _tagCommitsFromBaseBuild;
        TagCommit? _lastBuildInCI;
        TagCommit? _lastBuildInNonCI;

        internal SolutionVersionInfo( Solution solution,
                                      VersionTagInfo info,
                                      string builtTipSha,
                                      TagCommit lastAnyBuild,
                                      List<Commit> commitsFromBaseBuild,
                                      List<TagCommit> tagCommitsFromBaseBuild )
        {
            Throw.DebugAssert( info.HotZone != null && info.HotZone.HotZoneIssue == null );
            _solution = solution;
            _info = info;
            _builtTipSha = builtTipSha;
            _lastAnyBuild = lastAnyBuild;
            _commitsFromBaseBuild = commitsFromBaseBuild;
            _tagCommitsFromBaseBuild = tagCommitsFromBaseBuild;
        }


        internal bool IsDirty => _builtTipSha != _solution.GitSolution.GitBranch.Tip.Sha;

        /// <summary>
        /// Captures the last <see cref="TagCommit"/> to consider in a build context (branch and whether we are building
        /// regular or CI build).
        /// </summary>
        public readonly struct BuiltVersion
        {
            readonly SolutionVersionInfo _info;
            readonly TagCommit _tagCommit;

            internal BuiltVersion( SolutionVersionInfo info, TagCommit tagCommit )
            {
                _info = info;
                _tagCommit = tagCommit;
            }

            /// <summary>
            /// Gets the version tag.
            /// </summary>
            public TagCommit TagCommit => _tagCommit;

            /// <summary>
            /// Gets whether a build is required because <see cref="TagCommit"/> version is either
            /// a "+fake" or a "+deprecated" version and should not be used to reference any package
            /// produced by this solution.
            /// </summary>
            public bool VersionMustBuild => _tagCommit.BuildContentInfo == null;

            /// <summary>
            /// Gets whether a build is required because <see cref="TagCommit"/>'s content is not the same as
            /// the <see cref="GitSolution"/>'s git branch's tip content.
            /// </summary>
            public bool HasCodeChange => _tagCommit.Commit.Tree.Sha != _info._solution.GitSolution.GitBranch.Tip.Tree.Sha;

            /// <summary>
            /// Gets whether this solution has already been built: both <see cref="VersionMustBuild"/> and <see cref="HasCodeChange"/> are false.
            /// </summary>
            public bool IsAlreadyBuilt => !VersionMustBuild && !HasCodeChange;

            /// <summary>
            /// Overridden to return the <see cref="TagCommit"/>.
            /// </summary>
            /// <returns>The tag commit.</returns>
            public override string ToString() => _tagCommit.ToString();
        }

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _info.Repo;

        /// <summary>
        /// Gets the HotGraph solution.
        /// </summary>
        public Solution Solution => _solution;

        /// <inheritdoc cref="Solution.GitSolution" />
        public GitSolution GitSolution => _solution.GitSolution;

        /// <summary>
        /// Gets the version info of the <see cref="Repo"/>.
        /// </summary>
        public VersionTagInfo VersionTagInfo => _info;

        /// <summary>
        /// Gets the base commit that is the <see cref="VersionTagInfo.HotZoneInfo.LastStable"/>.
        /// Can be "+fake" or "+deprecated".
        /// </summary>
        public TagCommit BaseBuild => _info.HotZone!.LastStable;

        /// <summary>
        /// Gets the last built version to consider in the <see cref="Solution.Branch"/> and CI build context.
        /// </summary>
        public BuiltVersion LastBuildInCI
        {
            get
            {
                if( _lastBuildInCI == null )
                {
                    // Note: TagCommit.CompareTo reverts the SVersion.CompareTo order.
                    //       Using Min() here gives us the greatest version.
                    _lastBuildInCI = _tagCommitsFromBaseBuild.Min() ?? BaseBuild;
                }
                return new BuiltVersion( this, _lastBuildInCI );
            }
        }

        /// <summary>
        /// Gets the last built version to consider in the regular <see cref="Solution.Branch"/>.
        /// </summary>
        public BuiltVersion LastBuildInNonCI
        {
            get
            {
                if( _lastBuildInNonCI == null )
                {
                    Throw.CheckState( "Currently, only 'stable' branch is supported.", _solution.Branch.BranchName.Index == 0 );
                    // In the "stable" branch, the last commit is by design the BaseBuild.
                    _lastBuildInNonCI = BaseBuild;
                }
                return new BuiltVersion( this, _lastBuildInNonCI );
            }
        }

        /// <summary>
        /// Gets <see cref="LastBuildInCI"/> or <see cref="LastBuildInNonCI"/>.
        /// </summary>
        /// <param name="ciBuild">Whether we are in a CI build context.</param>
        /// <returns>The CI or non CI last build.</returns>
        public BuiltVersion GetLastBuild( bool ciBuild ) => ciBuild ? LastBuildInCI : LastBuildInNonCI;

        public bool VersionMustBuild => _lastAnyBuild.BuildContentInfo == null;

        /// <summary>
        /// Gets all the commits from <see cref="GitSolution"/>'s git branch's tip down to <see cref="BaseBuild"/>.
        /// </summary>
        public IReadOnlyList<Commit> CommitsFromBaseBuild => _commitsFromBaseBuild;

        /// <summary>
        /// Gets the <see cref="CommitsFromBaseBuild"/> joined with <see cref="VersionTagInfo.TagCommitsBySha"/>.
        /// </summary>
        public IReadOnlyList<TagCommit> TagCommitsFromBaseBuild => _tagCommitsFromBaseBuild;

    }
}
