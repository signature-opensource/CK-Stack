using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using System;
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
    readonly Dictionary<string, SVersion> _externalPackages;
    readonly Solution[] _solutions;
    readonly Dictionary<string, Solution> _p2s;
    ImmutableArray<Solution> _orderedSolutions;
    PackageUpdater? _packageUpdater;
    int _maxRank;

    internal HotGraph( BranchName branchName,
                       IReadOnlyList<Repo> allRepos,
                       IReadOnlyList<Repo> pivots,
                       VersionTagPlugin versionTags,
                       Dictionary<string, SVersion> externalPackages )
    {
        Throw.DebugAssert( allRepos.Count != pivots.Count || pivots == allRepos );
        _branchName = branchName;
        _allRepos = allRepos;
        _pivots = pivots;
        _versionTags = versionTags;
        _externalPackages = externalPackages;
        _p2s = new Dictionary<string, Solution>( StringComparer.OrdinalIgnoreCase );
        _solutions = new Solution[allRepos.Count];
    }

    /// <summary>
    /// Gets the branch name of this graph.
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
    /// Gets all the solutions (ordered by their <see cref="Solution.OrderedIndex"/>).
    /// </summary>
    public ImmutableArray<Solution> OrderedSolutions
    {
        get
        {
            if( _orderedSolutions.IsDefault )
            {
                _orderedSolutions = [.. _solutions.Order()];
                for( int i = 0; i < _orderedSolutions.Length; i++ )
                {
                    _orderedSolutions[i].OrderedIndex = i;
                }
            }
            return _orderedSolutions;
        } 
    }

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
                bool buildRequired = false;
                SolutionVersionInfo[] versions = new SolutionVersionInfo[ordered.Length];
                for( int i = 0; i < ordered.Length; i++ )
                {
                    Solution? s = ordered[i];
                    var sV = s.ComputeVersionInfo( monitor, _versionTags.GetWithoutIssue( monitor, s.Repo ) );
                    if( sV != null )
                    {
                        versions[i] = sV;
                        buildRequired |= sV.VersionMustBuild;
                    }
                    else
                    {
                        success = false;
                    }
                }
                if( success )
                {
                    return _packageUpdater = new PackageUpdater( this,
                                                                 ImmutableCollectionsMarshal.AsImmutableArray( versions ),
                                                                 buildRequired,
                                                                 _externalPackages );
                }
            }
        }
        return _packageUpdater;
    }

    internal bool AddSolution( IActivityMonitor monitor, Repo repo, HotBranch actual, GitSolution shallow, bool isPivot )
    {
        Throw.DebugAssert( _solutions[repo.Index] == null );
        Throw.DebugAssert( actual.GitBranch != null );
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

    internal bool TopologicalSort( IActivityMonitor monitor )
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
            if( !UpdateRank( monitor, sPivot, isPivotUpstream: null ) )
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
        Throw.DebugAssert( _solutions.Select( (s,idx) => s.Repo.Index == idx && s.Rank >= 0 && s.Rank <= _maxRank ).All( Util.FuncIdentity ) );
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
