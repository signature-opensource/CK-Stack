using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

public sealed partial class HotGraph
{

    /// <summary>
    /// Models a <see cref="HotGraph"/>'s solution bound to a <see cref="Branch"/>.
    /// </summary>
    public sealed class Solution : IComparable<Solution>
    {
        readonly HotGraph _graph;
        readonly HotBranch _actual;
        readonly GitSolution _solution;
        readonly List<Solution> _directRequirements;
        readonly HashSet<Solution> _allRequirements;
        SolutionVersionInfo? _versionInfo;
        string? _toString;
        int _rank;
        readonly bool _isPivot;
        bool _isPivotUpstream;
        bool _isPivotDownstream;

        internal Solution( HotGraph graph,
                           HotBranch actual,
                           GitSolution solution,
                           bool isPivot )
        {
            _graph = graph;
            _actual = actual;
            _solution = solution;
            _isPivot = isPivot;
            _directRequirements = new List<Solution>();
            _allRequirements = new HashSet<Solution>();
            _rank = -1;
        }

        /// <summary>
        /// Gets the graph that contains this solution.
        /// </summary>
        public HotGraph Graph => _graph;

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _solution.Repo;

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
        /// Gets the shallow solution. This is loaded from the "dev/<see cref="Branch"/>" branch if it exists or from the <see cref="Branch"/>.
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
        /// Gets the index of this solution in <see cref="HotGraph.OrderedSolutions"/>.
        /// </summary>
        public int OrderedIndex { get; internal set; }

        /// <summary>
        /// Gets the version related information.
        /// <see cref="HotGraph.GetPackageUpdater(IActivityMonitor)"/> must have been successfully called
        /// for this information to be available.
        /// </summary>
        public SolutionVersionInfo? VersionInfo => _versionInfo;

        internal SolutionVersionInfo? ComputeVersionInfo( IActivityMonitor monitor, VersionTagInfo? vInfo )
        {
            Throw.DebugAssert( _versionInfo == null );
            if( vInfo == null ) return null;
            Throw.DebugAssert( vInfo.Repo == Repo );
            // Temporary: this works for "stable" only scenario. With multiple
            // hot branches this will be more complicated.
            var lastBuild = vInfo.FindFirst( _solution.GitBranch.Commits, out _ );
            if( lastBuild == null )
            {
                monitor.Error( $"Unable to find a version tag for '{_solution}'." );
                return null;
            }
            if( lastBuild.BuildContentInfo == null )
            {
                monitor.Warn( $"Last build tag for '{_solution}' is '{lastBuild.Version.ParsedText}'. It requires a build." );
            }
            return _versionInfo = new SolutionVersionInfo( this, vInfo, lastBuild );
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

        internal bool UpdateRank( IActivityMonitor monitor,
                                  out int rank,
                                  [NotNullWhen(false)] ref List<string>? cycle,
                                  bool isPivotUpstream,
                                  out bool isPivotDownstream )
        {
            isPivotDownstream = _isPivotDownstream;
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
            _isPivotUpstream = isPivotUpstream;
            foreach( var c in _solution.Consumed )
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

                        _isPivotDownstream |= required.IsPivot;
                        ++reqRank;
                        _isPivotDownstream |= isThisPivotDownstream;
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
