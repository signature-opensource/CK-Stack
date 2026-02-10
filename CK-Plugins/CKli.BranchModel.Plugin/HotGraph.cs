using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Models a global dependency graph across all repositories.
/// </summary>
public sealed class HotGraph
{
    readonly BranchModelPlugin _branchModel;
    readonly BranchName _branchName;
    readonly IReadOnlyList<Repo> _allRepos;
    readonly HashSet<Repo> _pivots;
    readonly Solution[] _solutions;
    readonly Dictionary<string, Solution> _p2s;
    int _maxRank;

    internal HotGraph( BranchModelPlugin branchModel, BranchName branchName, IReadOnlyList<Repo> allRepos, HashSet<Repo> pivots )
    {
        _branchModel = branchModel;
        _branchName = branchName;
        _allRepos = allRepos;
        _pivots = pivots;
        _p2s = new Dictionary<string, Solution>();
        _solutions = new Solution[allRepos.Count];
    }

    /// <summary>
    /// Gets the initial branch name of this graph.
    /// <para>
    /// When <see cref="Pivot"/> has been specified, this branch necessarily exists in the Pivot.
    /// When no pivot exists, this branch exists in at least one of the World's repositories.
    /// </para>
    /// </summary>
    public BranchName BranchName => _branchName;

    /// <summary>
    /// Gets the pivots repositories if some has been specified.
    /// This is never empty: no pivot means all solutions are pivot.
    /// </summary>
    public IReadOnlySet<Repo> Pivots => _pivots;

    /// <summary>
    /// Gets whether not all solutions are pivots.
    /// </summary>
    public bool HasPivots => _pivots.Count != _allRepos.Count;

    /// <summary>
    /// Gets the ordered list of solutions by <see cref="Solution.Rank"/> and then by <see cref="Repo.Index"/>.
    /// </summary>
    public IReadOnlyCollection<Solution> Solutions => _solutions;

    /// <summary>
    /// Gets the maximal <see cref="Solution.Rank"/>.
    /// </summary>
    public int MaxRank => _maxRank;

    /// <summary>
    /// Models a solution to build.
    /// </summary>
    public sealed class Solution
    {
        readonly HotGraph _graph;
        readonly BranchName _actual;
        readonly GitSolution _solution;
        string? _toString;
        int _rank;
        bool _isPivotUpstream;
        bool _isPivotDownstream;

        internal Solution( HotGraph graph, BranchName actual, GitSolution solution )
        {
            _graph = graph;
            _actual = actual;
            _solution = solution;
            _rank = -1;
        }

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _solution.Repo;

        /// <summary>
        /// Gets the branch name from which this solution has been read.
        /// </summary>
        public BranchName BranchName => _actual;

        /// <summary>
        /// Gets the projects that are potentially packages produced by this solution.
        /// </summary>
        public IReadOnlyList<GitSolution.Project> Projects => _solution.Projects;

        /// <summary>
        /// Gets the build rank. From 0 (the first solution to build) to <see cref="HotGraph.MaxRank"/>.
        /// </summary>
        public int Rank => _rank;

        /// <summary>
        /// Gets whether this solution is one of the <see cref="HotGraph.Pivots"/>.
        /// </summary>
        public bool IsPivot => _graph._pivots.Contains( _solution.Repo );

        /// <summary>
        /// Gets whether this solution is a predecessor (a producer of packages) of one of the specified <see cref="HotGraph.Pivots"/>.
        /// </summary>
        public bool IsPivotUpstream => _isPivotUpstream;

        /// <summary>
        /// Gets whether this solution is a successor (a consumer) of one of the specified <see cref="HotGraph.Pivots"/>.
        /// </summary>
        public bool IsPivotDownstream => _isPivotDownstream;

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
                    _isPivotDownstream |= _graph._pivots.Contains( required.Repo );
                    if( !required.UpdateRank( monitor, out int reqRank, ref cycle, isPivotUpstream, out bool isThisPivotDownstream ) )
                    {
                        Throw.DebugAssert( cycle != null );
                        cycle ??= new List<string>();
                        cycle.Add( $"{c.PackageId} ({required.Repo.DisplayPath.LastPart})" );
                        return false;
                    }
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
            _rank = rank;
            return true;
        }

        /// <summary>
        /// Returns the repository and branch name.
        /// </summary>
        /// <returns>The logical path of this solution.</returns>
        public override string ToString() => _toString ??= $"{_solution.Repo.DisplayPath}/branch/{_actual}";
    }

    internal bool AddSolution( IActivityMonitor monitor, Repo repo, BranchName actual, GitSolution shallow )
    {
        Throw.DebugAssert( _solutions[repo.Index] == null );
        var s = new Solution( this, actual, shallow );
        _solutions[shallow.Repo.Index] = s;
        foreach( var p in shallow.Projects )
        {
            if( _p2s.TryGetValue( p.Name, out var exists ) )
            {
                // This project name is already mapped.

                // If the new project to add as a "package" is explicitly not packable, we have
                // no issue (whatever the existing one is).
                if( p.IsPackable is false ) continue;

                // Pathological case: the homonym project is the same solution.
                // Whether they are packaged or not, we don't allow this because it's a mess
                // and a recipe for disaster.
                if( exists == s )
                {
                    monitor.Error( $"The solution '{s}' contains duplicate project named '{p.Name}'." );
                    return false;
                }
                // Find the homonym project in the other solution (it necessarily exists).
                var pConflict = exists.Projects.First( pC => pC.Name == p.Name );

                // If both are null or true, we are in trouble. Otherwise it is the  
                if( p.IsPackable == pConflict.IsPackable )
                {
                    if( p.IsPackable is true )
                    {
                        monitor.Error( $"Both projects named '{p.Name}' in '{exists}' and '{s}' are <IsPackable>true</IsPackable>." );
                    }
                    else
                    {
                        monitor.Error( $"""
                            Projects named '{p.Name}' in '{exists}' and '{s}' don't specify <IsPackable /> value.
                            One of them must be specified to be packable (or not).
                            """ );
                    }
                    return false;
                }
                // p.IsPackable is true or null:
                // true => pConflict.IsPackable is false or null => p wins (replaces the mapping to s instead on exists).
                // null => pConflict.IsPackable is true or false
                //            pConflict.IsPackable is true => nothing to do.
                //            pConflict.IsPackable is false => Consider that p is "better than" pConflict => same as p.IsPackable is true.

                if( pConflict.IsPackable is true || pConflict.IsPackable is false )
                {
                    _p2s[p.Name] = s;
                }
            }
            else
            {
                _p2s.Add( p.Name, s );
            }
        }
        return true;
    }

    internal bool Sort( IActivityMonitor monitor )
    {
        Throw.DebugAssert( _solutions.All( s => s != null && _solutions[s.Repo.Index] == s ) );
        // Cycle detection is done by setting the _rank to -2 AND checking that it is
        // not -2 when entering a node: when it happens this instantiates the cycles collector
        // that is filled while rewinding the stack.
        // This should barely happen.
        List<string>? cycle = null;
        // Sorts the pivots by index to be stable.
        IEnumerable<Repo> pivots = _pivots.Count == _solutions.Length
                                    ? _allRepos
                                    : _pivots.OrderBy( r => r.Index );
        foreach( var pivot in pivots )
        {
            var sPivot = _solutions[pivot.Index];
            if( !sPivot.UpdateRank( monitor, out _, ref cycle, isPivotUpstream: true, out _ ) )
            {
                monitor.Error( $"Cycle detected between solutions: {cycle.Concatenate()}." );
                return false;
            }

        }
        if( _pivots.Count != _solutions.Length )
        {
            foreach( var s in _solutions )
            {
                if( !s.UpdateRank( monitor, out _, ref cycle, isPivotUpstream: false, out _ ) )
                {
                    monitor.Error( $"Cycle detected between solutions: {cycle.Concatenate()}." );
                    return false;
                }
            }
        }
        Throw.DebugAssert( _solutions.All( s => s.Rank >= 0 && s.Rank <= _maxRank ) );
        _solutions.Sort( (s1,s2) =>
        {
            int cmp = s1.Rank.CompareTo( s2.Rank );
            if( cmp != 0 ) return cmp;
            return s1.Repo.Index.CompareTo( s2.Repo.Index );
        } );
        return true;
    }
}
