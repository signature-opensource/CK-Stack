using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static CKli.BranchModel.Plugin.HotGraph;

namespace CKli.BranchModel.Plugin;

public sealed partial class HotGraph
{
    /// <summary>
    /// Models a <see cref="HotGraph"/>'s solution bound to a <see cref="Branch"/>.
    /// </summary>
    public sealed class Solution : IComparable<Solution>
    {
        readonly HotGraph _graph;
        readonly Repo _repo;
        readonly HotBranch _actual;
        readonly List<Solution> _directRequirements;
        readonly HashSet<Solution> _allRequirements;
        readonly List<PackageInstance> _externalDependencies;
        readonly bool _isPivot;
        string? _toString;

        internal Solution? _nextDevSolution;
        GitSolution _solution;
        SolutionVersionInfo? _versionInfo;
        int _rank;
        bool _isPivotUpstream;
        bool _isPivotDownstream;
        bool _isDevSolution;

        internal Solution( HotGraph graph,
                           Repo repo,
                           HotBranch actual,
                           GitSolution solution,
                           bool isPivot,
                           bool isDevSolution )
        {
            _graph = graph;
            _repo = repo;
            _actual = actual;
            _solution = solution;
            _isPivot = isPivot;
            _directRequirements = new List<Solution>();
            _allRequirements = new HashSet<Solution>();
            _externalDependencies = new List<PackageInstance>();
            _rank = -1;
            if( isDevSolution )
            {
                SetDevSolution( graph, solution );
            }
        }

        internal GitSolution SetDevSolution( HotGraph graph, GitSolution solution )
        {
            Throw.DebugAssert( !_isDevSolution );
            _isDevSolution = true;
            _nextDevSolution = graph._firstDevSolution;
            graph._firstDevSolution = this;
            ++graph._devSolutionCount;
            var previous = _solution;
            _solution = solution;
            return previous;
        }

        /// <summary>
        /// Gets the graph that contains this solution.
        /// </summary>
        public HotGraph Graph => _graph;

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _repo;

        /// <summary>
        /// Gets the branch name from which this <see cref="GitSolution"/> has been read: it is the closest
        /// active branch from the <see cref="HotGraph.BranchName"/>.
        /// </summary>
        public HotBranch Branch => _actual;

        /// <summary>
        /// Gets the build rank. From 0 (the first solutions to build) to <see cref="HotGraph.MaxRank"/>.
        /// </summary>
        public int Rank => _rank;

        /// <summary>
        /// Gets the direct solutions that produce at least one of the <see cref="GitSolution.Consumed"/> packages.
        /// </summary>
        public IReadOnlyList<Solution> DirectRequirements => _directRequirements;

        /// <summary>
        /// Gets the closure of the <see cref="DirectRequirements"/>.
        /// </summary>
        public IReadOnlySet<Solution> AllRequirements => _allRequirements;

        /// <summary>
        /// Gets the shallow solution. This may loaded from the "dev/<see cref="Branch"/>" branch if it exists and <see cref="IsDevSolution"/>
        /// is true, or from the regular <see cref="Branch"/>.
        /// </summary>
        public GitSolution GitSolution => _solution;

        /// <summary>
        /// Gets whether this solution is one of the <see cref="HotGraph.Pivots"/>.
        /// <para>
        /// This is false when <see cref="HotGraph.HasPivots"/> is false. Since in this case every solution is a (or is not a) pivot,
        /// we chose to to keep false for everything (<see cref="IsPivotUpstream"/> and <see cref="IsPivotUpstream"/> are also false).
        /// </para>
        /// </summary>
        public bool IsPivot => _isPivot;

        /// <summary>
        /// Gets whether this solution is a predecessor (a producer of packages) of one of the specified <see cref="HotGraph.Pivots"/>.
        /// </summary>
        public bool IsPivotUpstream => _isPivotUpstream;

        /// <summary>
        /// Gets whether this solution is a successor (a consumer) of one of the specified <see cref="HotGraph.Pivots"/>.
        /// </summary>
        public bool IsPivotDownstream => _isPivotDownstream;

        /// <summary>
        /// Gets whether this solution belongs to the current <see cref="HotGraph.DevSolutions"/>.
        /// </summary>
        public bool IsDevSolution => _isDevSolution;

        /// <summary>
        /// Gets whether this solution can be <see cref="IsDevSolution"/>: this <see cref="HotBranch"/> is the <see cref="HotGraph.BranchName"/>.
        /// </summary>
        public bool CanBeDevSolution => _actual.BranchName == _graph.BranchName;

        /// <summary>
        /// Gets the index of this solution in <see cref="HotGraph.OrderedSolutions"/>.
        /// </summary>
        public int OrderedIndex { get; internal set; }

        /// <summary>
        /// Gets the version related information.
        /// <see cref="HotGraph.GetPackageUpdater(IActivityMonitor)"/> must have been successfully called
        /// for this information to be available.
        /// </summary>
        public SolutionVersionInfo? VersionInfo => _versionInfo;

        /// <summary>
        /// Gets the dependencies to external packages (not produced by the stack itself).
        /// </summary>
        public IReadOnlyList<PackageInstance> ExternalDependencies => _externalDependencies;

        internal SolutionVersionInfo? ComputeVersionInfo( IActivityMonitor monitor, VersionTagInfo? vInfo )
        {
            Throw.DebugAssert( _versionInfo == null );
            Throw.DebugAssert( _solution != null );
            if( vInfo == null ) return null;

            Throw.DebugAssert( vInfo.Repo == Repo );
            Throw.DebugAssert( !vInfo.HasIssue );

            var baseTagCommit = vInfo.HotZone.LastStable;
            var commitsLog = Repo.GitRepository.Repository.Commits.QueryBy( new CommitFilter()
            {
                IncludeReachableFrom = _solution.GitBranch.Tip,
                ExcludeReachableFrom = baseTagCommit.Commit,
                SortBy = CommitSortStrategies.Time | CommitSortStrategies.Reverse
            } );
            var commitsFromBaseBuild = commitsLog.ToList();

            var tagCommitsFromBaseBuild = new List<TagCommit>();
            foreach( var c in commitsFromBaseBuild )
            {
                var tc = vInfo.TagCommitsBySha.GetValueOrDefault( c.Sha );
                if( tc != null )
                {
                    var v = tc.Version;
                    if( v < baseTagCommit.Version )
                    {
                        monitor.Warn( $"Ignoring {tc} in '{_solution}' as it is lower than the last version '{baseTagCommit.Version.ParsedText}'." );
                    }
                    else
                    {
                        tagCommitsFromBaseBuild.Add( tc );
                    }
                }
            }
            // Temporary: this works for "stable" only scenario. With multiple
            // hot branches this will be more complicated.
            //
            // Note: TagCommit.CompareTo reverts the SVersion.CompareTo order.
            //
            var lastAnyBuild = tagCommitsFromBaseBuild.Min() ?? baseTagCommit;
            if( lastAnyBuild.BuildContentInfo == null )
            {
                monitor.Warn( $"Last any build tag for '{_solution}' is '{lastAnyBuild.Version.ParsedText}'. It requires a build." );
            }
            return _versionInfo = new SolutionVersionInfo( this, vInfo, lastAnyBuild, commitsFromBaseBuild, tagCommitsFromBaseBuild );
        }

        /// <summary>
        /// This drives the <see cref="HotGraph.OrderedSolutions"/> list.
        /// </summary>
        int IComparable<Solution>.CompareTo( Solution? other )
        {
            if( other == null ) return 1;
            int cmp = _rank.CompareTo( other._rank );
            return cmp != 0 ? cmp : _solution.Repo.Index.CompareTo( other._solution.Repo.Index );
        }

        internal void ResetRank()
        {
            Throw.DebugAssert( _rank >= 0 );
            _rank = -1;
            _isPivotUpstream = false;
            _isPivotDownstream = false;
            _directRequirements.Clear();
            _allRequirements.Clear();
            _externalDependencies.Clear();
            _versionInfo = null;
        }

        internal bool UpdateRank( IActivityMonitor monitor,
                                  out int rank,
                                  [NotNullWhen(false)] ref List<string>? cycle,
                                  bool? isPivotUpstream,
                                  out bool isPivotDownstream )
        {
            isPivotDownstream = _isPivotDownstream;
            // Upgrade if a pivot requires this.
            if( isPivotUpstream.HasValue )
            {
                _isPivotUpstream |= isPivotUpstream.Value;
            }
            rank = _rank;
            if( _rank >= 0 )
            {
                return true;
            }
            if( _rank == -2 )
            {
                cycle = new List<string>();
                return false;
            }
            Throw.DebugAssert( rank == -1 );
            _rank = -2;
            rank = 0;
            if( !isPivotUpstream.HasValue )
            {
                _isPivotUpstream = false;
                isPivotUpstream = _isPivot;
            }
            foreach( PackageInstance c in _solution.Consumed )
            {
                if( _graph._p2s.TryGetValue( c.PackageId, out Solution? required ) )
                {
                    if( !required.UpdateRank( monitor, out int reqRank, ref cycle, isPivotUpstream, out bool isThisPivotDownstream ) )
                    {
                        Throw.DebugAssert( cycle != null );
                        cycle ??= new List<string>();
                        cycle.Add( $"{c.PackageId} ({required.Repo.DisplayPath.LastPart})" );
                        return false;
                    }
                    //
                    // This must be done only once per requirement.
                    // We use a simple list for direct requirements (rather small list) but
                    // a set for the closure of the requirements.
                    //
                    if( !_directRequirements.Contains( required ) )
                    {
                        _directRequirements.Add( required );
                        // Trick: here the direct requirement is not added, only its closure is.
                        _allRequirements.AddRange( required._allRequirements );

                        _isPivotDownstream |= isThisPivotDownstream | required.IsPivot;
                        ++reqRank;
                        if( rank < reqRank )
                        {
                            rank = reqRank;
                            if( _graph._maxRank < rank )
                            {
                                _graph._maxRank = rank;
                            }
                        }
                    }
                }
                else
                {
                    // This package identifier doesn't match any stack's solutions' project: this is an external
                    // dependency.
                    // We collect them here, tracking discrepancies among them is done on demand.
                    _externalDependencies.Add( c );
                }
            }
            // Finalize direct and closure requirements.
            for( int i = 0; i < _directRequirements.Count; ++i )
            {
                var d = _directRequirements[i];
                // If the direct requirement d is already in the closure, then it is not a direct requirement.
                // Otherwise, d is direct and it must also appear in the closure: the Add just does that.
                if( !_allRequirements.Add( d ) )
                {
                    _directRequirements.RemoveAt( i-- );
                }
            }
            isPivotDownstream = _isPivotDownstream;
            _rank = rank;
            return true;
        }

        /// <summary>
        /// Returns the repository and branch name.
        /// </summary>
        /// <returns>The logical path of this solution.</returns>
        public override string ToString() => _toString ??= $"{_solution.Repo.DisplayPath} ({_actual})";
    }
}
