using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.BranchModel.Plugin;


/// <summary>
/// Models a global dependency graph across all repositories.
/// </summary>
public sealed partial class HotGraph
{
    readonly BranchModelPlugin _branchModel;
    readonly VersionTagPlugin _versionTag;
    readonly BranchName _branchName;
    readonly IReadOnlyList<Repo> _allRepos;
    readonly HashSet<Repo> _pivots;
    readonly Solution[] _solutions;
    readonly Dictionary<string, Solution> _p2s;
    int _maxRank;

    internal HotGraph( BranchModelPlugin branchModel,
                       VersionTagPlugin versionTag,
                       BranchName branchName,
                       IReadOnlyList<Repo> allRepos,
                       HashSet<Repo> pivots )
    {
        _branchModel = branchModel;
        _versionTag = versionTag;
        _branchName = branchName;
        _allRepos = allRepos;
        _pivots = pivots;
        _p2s = new Dictionary<string, Solution>();
        _solutions = new Solution[allRepos.Count];
    }

    /// <summary>
    /// Gets the initial branch name of this graph.
    /// <para>
    /// When <see cref="Pivots"/> have been specified, this branch necessarily exists in the Pivots.
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

    internal bool AddSolution( IActivityMonitor monitor, Repo repo, HotBranch actual, GitSolution shallow, bool isPivot )
    {
        Throw.DebugAssert( _solutions[repo.Index] == null );
        Throw.DebugAssert( actual.GitBranch != null );
        var versionInfo = _versionTag.GetWithoutIssue( monitor, repo );
        if( versionInfo == null )
        {
            return false;
        }
        var s = new Solution( this, actual, shallow, isPivot );
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
                var pConflict = exists.GitSolution.Projects.First( pC => pC.Name == p.Name );

                // If both are null or true, we are in trouble.
                // (We cannot be here if p.IsPackable is false!)
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
                            One of them must be specified to be packable (or not) in project files:
                            {exists}: {pConflict.Path}
                            {s}: {p.Path}
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
        // Sorts the pivots by index to be deterministic.
        bool hasPivots = HasPivots;
        IEnumerable<Repo> pivots = hasPivots
                                    ? _pivots.OrderBy( r => r.Index )
                                    : _allRepos;
        foreach( var pivot in pivots )
        {
            var sPivot = _solutions[pivot.Index];
            if( !UpdateRank( monitor, sPivot, isPivotUpstream: hasPivots ) )
            {
                return false;
            }

        }
        if( hasPivots )
        {
            foreach( var s in _solutions )
            {
                if( !UpdateRank( monitor, s, isPivotUpstream: false ) )
                {
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

        static bool UpdateRank( IActivityMonitor monitor, Solution sPivot, bool isPivotUpstream )
        {
            // Cycle detection is done by setting the _rank to -2 AND checking that it is
            // not -2 when entering a node: when it happens this instantiates the cycles collector
            // that is filled while rewinding the stack.
            // This should barely happen.
            List<string>? cycle = null;
            if( !sPivot.UpdateRank( monitor, out _, ref cycle, isPivotUpstream, out _ ) )
            {
                monitor.Error( $"Cycle detected between solutions: {cycle.Concatenate()}." );
                return false;
            }
            return true;
        }
    }
}
