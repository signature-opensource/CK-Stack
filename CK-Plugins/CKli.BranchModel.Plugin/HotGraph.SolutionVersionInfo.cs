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
        readonly TagCommit _lastBuild;
        IReadOnlyList<Commit>? _commitsFromBaseBuild;
        IReadOnlyList<TagCommit>? _tagCommitsFromBaseBuild;

        internal SolutionVersionInfo( Solution solution, VersionTagInfo info, TagCommit lastBuild )
        {
            Throw.DebugAssert( info.HotZone != null && info.HotZone.HotZoneIssue == null );
            _solution = solution;
            _info = info;
            _lastBuild = lastBuild;
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
        /// </summary>
        public TagCommit BaseBuild => _info.HotZone!.LastStable;

        /// <summary>
        /// Gets the last built version to consider in the <see cref="Solution.Branch"/>.
        /// Note that if <see cref="VersionMustBuild"/> is true, this tag commit is either
        /// a "+fake" or a "+deprecated" version and should not be used.
        /// </summary>
        public TagCommit LastBuild => _lastBuild;

        /// <summary>
        /// Gets whether this solution must be built: its <see cref="LastBuild"/> version is either
        /// a "+fake" or a "+deprecated" version and should not be used to reference any package
        /// produced by this solution.
        /// </summary>
        public bool VersionMustBuild => _lastBuild.BuildContentInfo == null;

        /// <summary>
        /// Gets whether this <see cref="LastBuild"/>'s commit is not the same as the <see cref="GitSolution"/>'s git branch's tip.
        /// </summary>
        public bool HasCodeChange => _lastBuild.Commit.Sha != _solution.GitSolution.GitBranch.Tip.Sha;

        /// <summary>
        /// Gets whether this solution has already been built: both <see cref="VersionMustBuild"/> and <see cref="HasCodeChange"/> are false.
        /// </summary>
        public bool IsAlreadyBuilt => !VersionMustBuild && !HasCodeChange;

        /// <summary>
        /// Gets all the commits from <see cref="GitSolution"/>'s git branch's tip down to <see cref="LastBuild"/>.
        /// </summary>
        public IReadOnlyList<Commit> CommitsFromBaseBuild
        {
            get
            {
                if( _commitsFromBaseBuild == null )
                {
                    var commitsLog = Repo.GitRepository.Repository.Commits.QueryBy( new CommitFilter()
                    {
                        IncludeReachableFrom = _solution.GitSolution.GitBranch.Tip,
                        ExcludeReachableFrom = BaseBuild.Commit,
                        SortBy = CommitSortStrategies.Time | CommitSortStrategies.Reverse
                    } );
                    _commitsFromBaseBuild = commitsLog.ToList();
                }
                return _commitsFromBaseBuild;
            }
        }

        /// <summary>
        /// Gets the <see cref="CommitsFromBaseBuild"/> joined with <see cref="VersionTagInfo.TagCommitsBySha"/>.
        /// </summary>
        public IReadOnlyList<TagCommit> TagCommitsFromBaseBuild
        {
            get
            {
                if( _tagCommitsFromBaseBuild == null )
                {
                    _tagCommitsFromBaseBuild = CommitsFromBaseBuild.Select( c => _info.TagCommitsBySha.GetValueOrDefault( c.Sha ) )
                                                                   .Where( tc => tc != null )
                                                                   .ToList()!;
                }
                return _tagCommitsFromBaseBuild;
            }
        }


    }
}
