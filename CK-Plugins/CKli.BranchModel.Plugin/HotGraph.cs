using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.BranchModel.Plugin;


/// <summary>
/// Models a global dependency graph across all repositories.
/// <para>
/// Created by <see cref="BranchModelPlugin.GetHotGraph(IActivityMonitor, BranchName, IReadOnlyList{Repo})"/>.
/// This implies that <see cref="BranchModelInfo.HasIssue"/> is false but doesn't depend on the <see cref="VersionTagPlugin"/>.
/// <para>
/// version tags issues don't prevent a HotGraph to be obtained. They prevent <see cref="PackageUpdater"/>
/// and <see cref="SolutionVersionInfo"/> to be obtained (and eventually CKli.BuildPlugin.Roadmap creation).
/// </para>
/// </summary>
public sealed partial class HotGraph
{
    readonly BranchName _branchName;
    readonly IReadOnlyList<Repo> _allRepos;
    readonly IReadOnlyList<Repo> _pivots;
    readonly VersionTagPlugin _versionTags;
    readonly IReadOnlyDictionary<string, SVersion> _externalPackages;
    readonly ShallowSolutionPlugin _shallowSolution;
    readonly DevSolutionCollection _devSolutions;
    // Following fields are mutable, reset by OnSolutionChange.
    readonly Solution[] _solutions;
    readonly Dictionary<string, Solution> _p2s;
    Solution[] _orderedSolutions;
    PackageUpdater? _packageUpdater;
    Solution? _firstDevSolution;
    int _devSolutionCount;
    int _maxRank;

    sealed class DevSolutionCollection( HotGraph g ) : IReadOnlyCollection<Solution>
    {
        public int Count => g._devSolutionCount;

        public IEnumerator<Solution> GetEnumerator()
        {
            var d = g._firstDevSolution;
            while( d != null )
            {
                yield return d;
                d = d._nextDevSolution;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal HotGraph( BranchName branchName,
                       IReadOnlyList<Repo> allRepos,
                       IReadOnlyList<Repo> pivots,
                       VersionTagPlugin versionTags,
                       ShallowSolutionPlugin shallowSolution,
                       IReadOnlyDictionary<string, SVersion> externalPackages )
    {
        Throw.DebugAssert( allRepos.Count != pivots.Count || pivots == allRepos );
        Throw.DebugAssert( pivots.Select( p => p.Index ).IsSortedStrict() );
        _branchName = branchName;
        _allRepos = allRepos;
        _pivots = pivots;
        _versionTags = versionTags;
        _shallowSolution = shallowSolution;
        _externalPackages = externalPackages;
        _devSolutions = new DevSolutionCollection( this );
        _p2s = new Dictionary<string, Solution>( StringComparer.OrdinalIgnoreCase );
        _solutions = new Solution[allRepos.Count];
        _orderedSolutions = new Solution[allRepos.Count];
        _maxRank = -1;
    }

    /// <summary>
    /// Gets the theoretical branch name of this graph.
    /// </summary>
    public BranchName BranchName => _branchName;

    /// <summary>
    /// Gets the pivots repositories if some has been specified.
    /// This is never empty (no pivot means all repositories are pivot).
    /// <para>
    /// This list is ordered by <see cref="Repo.Index"/>.
    /// </para>
    /// </summary>
    public IReadOnlyList<Repo> Pivots => _pivots;

    /// <summary>
    /// Gets whether there are pivots: <see cref="Pivots"/> are not the same as <see cref="Solutions"/>.
    /// </summary>
    public bool HasPivots => _pivots != _allRepos;

    /// <summary>
    /// Gets all the solutions (ordered by <see cref="Repo.Index"/>).
    /// </summary>
    public IReadOnlyList<Solution> Solutions => _solutions;

    /// <summary>
    /// Gets the subset of the <see cref="Solutions"/> for which the <see cref="HotBranch.GitDevBranch"/> must be considered
    /// (if it exists) instead of the regular branch.
    /// </summary>
    public IReadOnlyCollection<Solution> DevSolutions => _devSolutions;

    /// <summary>
    /// Gets all the solutions (ordered by their <see cref="Solution.Rank"/> and then
    /// by <see cref="Repo.Index"/>): the <see cref="Solution.OrderedIndex"/> reflects this order.
    /// </summary>
    public IReadOnlyList<Solution> OrderedSolutions => _orderedSolutions;

    /// <summary>
    /// Gets the all the package identifiers that this graph produces mapped to their <see cref="Solution"/>.
    /// </summary>
    public IReadOnlyDictionary<string, Solution> ProducedPackages => _p2s;

    /// <summary>
    /// Gets the maximal <see cref="Solution.Rank"/>.
    /// </summary>
    public int MaxRank => _maxRank;

    /// <summary>
    /// Gets the package updater for this graph.
    /// This requires that no version related issue exist (all <see cref="VersionTagInfo.HasIssue"/> are false).
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <returns>The package updater or null on error.</returns>
    public PackageUpdater? GetPackageUpdater( IActivityMonitor monitor )
    {
        if( _packageUpdater == null )
        {
            using( monitor.OpenInfo( "Computing graph's PackageUpdater." ) )
            {
                var ordered = OrderedSolutions;
                bool success = true;
                SolutionVersionInfo[] versions = new SolutionVersionInfo[_solutions.Length];
                for( int i = 0; i < _solutions.Length; i++ )
                {
                    Solution? s = _solutions[i];
                    var sV = s.ComputeVersionInfo( monitor, _versionTags.GetWithoutIssue( monitor, s.Repo ) );
                    if( sV != null )
                    {
                        versions[i] = sV;
                    }
                    else
                    {
                        success = false;
                    }
                }
                if( success )
                {
                    return _packageUpdater = new PackageUpdater( this, ImmutableCollectionsMarshal.AsImmutableArray( versions ) );
                }
            }
        }
        return _packageUpdater;
    }

    /// <summary>
    /// Mutate this graph by adding the <paramref name="solutions"/> to the <see cref="DevSolutions"/>.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="solutions">The solutions.</param>
    /// <returns>True on success, false on error.</returns>
    public bool ConsiderBuildImpact( IActivityMonitor monitor, IEnumerable<Solution> solutions )
    {
        bool success = true;
        foreach( var s in solutions )
        {
            if( s.CanBeDevSolution && !s.IsDevSolution )
            {
                Throw.CheckArgument( s.Graph == this );
                var devBranch = s.Branch.GitDevBranch;
                if( devBranch != null )
                {
                    var newSolution = _shallowSolution.GetShallowSolution( monitor, s.Repo, devBranch );
                    if( newSolution == null )
                    {
                        success = false;
                    }
                    else
                    {
                        var prevSolution = s.SetDevSolution( this, newSolution );
                        ResetSort();
                        UnregisterProjects( s, prevSolution );
                        success &= RegisterProjects( monitor, s, newSolution );
                    }
                }
            }
        }
        if( success  && _maxRank == -1 )
        {
            success &= TopologicalSort( monitor );
        }
        return success;
    }

    internal bool AddSolution( IActivityMonitor monitor, Repo repo, HotBranch actual, bool isPivot, bool isActiveDev )
    {
        Throw.DebugAssert( _solutions[repo.Index] == null );
        Throw.DebugAssert( actual.GitBranch != null );
        Throw.DebugAssert( "IsDevActive => We are on the theoretical graph branch.", !isActiveDev || actual.BranchName == _branchName );

        var shallow = _shallowSolution.GetShallowSolution( monitor, repo, (isActiveDev ? actual.GitDevBranch : null) ?? actual.GitBranch );
        if( shallow == null ) return false;

        var s = new Solution( this, repo, actual, shallow, isPivot, isActiveDev );
        _solutions[repo.Index] = s;
        _orderedSolutions[repo.Index] = s;
        return RegisterProjects( monitor, s, shallow );
    }

    void UnregisterProjects( Solution s, GitSolution shallow )
    {
        List<string> projectNames = new List<string>( shallow.Projects.Count );
        foreach( var (name,solution) in _p2s )
        {
            if( solution == s )
            {
                Throw.DebugAssert( "Homonym projects in the same solution: this has been rejected by RegisterProjects.",
                                   shallow.Projects.Count( p => p.Name == name ) == 1 );
                projectNames.Add( name );
            }
        }
        foreach( var n in projectNames )
        {
            _p2s.Remove( n );
        }
    }

    bool RegisterProjects( IActivityMonitor monitor, Solution s, GitSolution shallow )
    {
        Throw.DebugAssert( "No project are currently registered for this Solution.", !_p2s.Values.Contains( s ) );
        foreach( var p in shallow.Projects )
        {
            if( _p2s.TryGetValue( p.Name, out var exists ) )
            {
                // This project name is already mapped.

                // If the new project to add as a "package" is explicitly not packable, we have
                // no issue (whatever the existing one is).
                if( p.IsPackable is false ) continue;

                // Pathological case: the homonym project is in the same solution.
                // Whether they are packaged or not, we don't allow this because it's a mess
                // and a recipe for disaster.
                if( exists == s )
                {
                    monitor.Error( $"The solution '{s}' contains duplicate project named '{p.Name}'." );
                    return false;
                }
                // Find the homonym project in the other solution (it necessarily exists).
                var pConflict = exists.GitSolution.Projects.First( pC => StringComparer.OrdinalIgnoreCase.Equals( pC.Name, p.Name ) );

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

    void ResetSort()
    {
        if( _maxRank >= 0 )
        {
            foreach( var s in _solutions )
            {
                s.ResetRank();
            }
            _packageUpdater = null;
            _maxRank = -1;
        }
    }

    internal bool TopologicalSort( IActivityMonitor monitor )
    {
        Throw.DebugAssert( _solutions.All( s => s != null && _solutions[s.Repo.Index] == s ) );
        Throw.DebugAssert( _maxRank == -1 );
        _maxRank = 0;
        foreach( var pivot in _pivots )
        {
            var sPivot = _solutions[pivot.Index];
            if( !UpdateRank( monitor, sPivot, isPivotUpstream: null ) )
            {
                return false;
            }
        }
        if( HasPivots )
        {
            foreach( var s in _solutions )
            {
                if( !UpdateRank( monitor, s, isPivotUpstream: false ) )
                {
                    return false;
                }
            }
        }
        Throw.DebugAssert( _solutions.Select( (s,idx) => s.Repo.Index == idx && s.Rank >= 0 && s.Rank <= _maxRank ).All( Util.FuncIdentity ) );
        Array.Sort( _orderedSolutions );
        for( int i = 0; i < _orderedSolutions.Length; ++i )
        {
            _orderedSolutions[i].OrderedIndex = i;
        }
        return true;

        static bool UpdateRank( IActivityMonitor monitor, Solution sPivot, bool? isPivotUpstream )
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
