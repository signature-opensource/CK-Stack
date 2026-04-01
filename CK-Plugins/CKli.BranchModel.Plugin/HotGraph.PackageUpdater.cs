using CK.Core;
using CKli.ShallowSolution.Plugin;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CKli.BranchModel.Plugin;


public sealed partial class HotGraph
{
    /// <summary>
    /// Exposes the <see cref="PackageMapping"/> that must be used to update package dependencies
    /// in the <see cref="Graph"/>.
    /// <para>
    /// Obtained by <see cref="HotGraph.GetPackageUpdater(IActivityMonitor)"/>. Requires that <see cref="VersionTagPlugin"/> has no issue.
    /// </para>
    /// </summary>
    public sealed class PackageUpdater
    {
        readonly HotGraph _graph;
        readonly ImmutableArray<SolutionVersionInfo> _versions;
        readonly bool _buildRequired;
        IReadOnlyDictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>>? _discrepancies;

        Mapping? _packageMapping;

        internal PackageUpdater( HotGraph graph,
                                 ImmutableArray<SolutionVersionInfo> versions,
                                 bool buildRequired )
        {
            _graph = graph;
            _versions = versions;
            _buildRequired = buildRequired;
        }

        /// <summary>
        /// Gets the hot graph.
        /// </summary>
        public HotGraph Graph => _graph;

        /// <summary>
        /// Gets the package mapping with the current <see cref="SolutionVersionInfo.LastBuild"/> version of each solution
        /// even if <see cref="SolutionVersionInfo.VersionMustBuild"/> is true (the version can be a "+fake" or a "+deprecated").
        /// <para>
        /// This also integrates the <see cref="BranchModelPlugin.GetExternalPackages(IActivityMonitor)"/> configured 
        /// </para>
        /// </summary>
        public IPackageMapping PackageMapping => _packageMapping ??= new Mapping( _graph._p2s, _versions, _graph._externalPackages, Discrepancies );

        /// <summary>
        /// Gets whether at least one <see cref="SolutionVersionInfo.VersionMustBuild"/> is true: the <see cref="PackageMapping"/> should not
        /// be used as-is, packages should be updated during a build, not directly.
        /// </summary>
        public bool BuildRequired => _buildRequired;

        /// <summary>
        /// Maps package name to one or more Solution in which their <see cref="Solution.ExternalDependencies"/> have different versions.
        /// <para>
        /// A solution may appear more than once if more than one of its project references different versions.
        /// </para>
        /// <para>
        /// This is computed on demand and cached. The <see cref="PackageMapping"/> automatically considers the greatest version among
        /// the references to be the one to use. This supports a rather easy way to handle version unification in a World: it is enough
        /// to upgrade one project in one solution to propagate the upgrade to all the Repos. 
        /// </para>
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>> Discrepancies
        {
            get
            {
                if( _discrepancies == null )
                {
                    var firstMet = new Dictionary<string, (Solution Solution, SVersion Version)>();
                    var result = new Dictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>>();
                    foreach( var s in _versions )
                    {
                        foreach( var p in s.Solution.ExternalDependencies )
                        {
                            if( firstMet.TryGetValue( p.PackageId, out var exists ) )
                            {
                                if( exists.Version != p.Version )
                                {
                                    AddDiscrepancy( result, p.PackageId, exists, (s.Solution, p.Version) );
                                }
                            }
                            else
                            {
                                firstMet.Add( p.PackageId, (s.Solution, p.Version) );
                            }
                        }
                    }
                    _discrepancies = result;

                    static void AddDiscrepancy( Dictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>> result,
                                                string packageId,
                                                in (Solution Solution, SVersion Version) exists,
                                                in (Solution Solution, SVersion Version) clash )
                    {
                        if( result.TryGetValue( packageId, out var clashes ) )
                        {
                            Unsafe.As<List<(Solution Solution, SVersion Version)>>( clashes ).Add( clash );
                        }
                        else
                        {
                            result.Add( packageId, new List<(Solution Solution, SVersion Version)> { exists, clash } );
                        }
                    }
                }
                return _discrepancies;
            }

        }

        /// <summary>
        /// Gets whether the solution has at least one dependency that must be updated.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <returns>True if the dependencies should be updated, false otherwise.</returns>
        public bool HasUpdates( Solution solution )
        {
            Throw.CheckArgument( solution.Graph == Graph );
            var mapping = PackageMapping;
            return solution.GitSolution.Consumed.Any( p => mapping.TryGetMappedVersion( p.PackageId, p.Version, out var version )
                                                           && p.Version != version );
        }

        /// <summary>
        /// Gets whether the solution has at least one dependency that must be updated and collects these updates.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <param name="updates">The updates.</param>
        /// <returns>True if the dependencies should be updated, false otherwise.</returns>
        public bool HasUpdates( Solution solution, PackageMapper updates )
        {
            Throw.CheckArgument( solution.Graph == Graph );
            var mapping = PackageMapping;
            bool hasUpdates = false;
            foreach( var p in solution.GitSolution.Consumed )
            {
                if( mapping.TryGetMappedVersion( p.PackageId, p.Version, out var version )
                    && p.Version != version )
                {
                    updates.Add( p.PackageId, p.Version, version );
                    hasUpdates = true;
                }
            }
            return hasUpdates;
        }

        /// <summary>
        /// Gets whether the solution has at least one dependency that must be updated and collects these updates.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <param name="updates">On success, the non null updates that must be done.</param>
        /// <returns>True if the dependencies should be updated, false otherwise.</returns>
        public bool HasUpdates( Solution solution, [NotNullWhen(true)] out PackageMapper? updates )
        {
            Throw.CheckArgument( solution.Graph == Graph );
            updates = null;
            var mapping = PackageMapping;
            foreach( var p in solution.GitSolution.Consumed )
            {
                if( mapping.TryGetMappedVersion( p.PackageId, p.Version, out var version )
                    && p.Version != version )
                {
                    updates ??= new PackageMapper();
                    updates.Add( p.PackageId, p.Version, version );
                }
            }
            return updates != null;
        }

        sealed class Mapping : IPackageMapping
        {
            readonly Dictionary<string, Solution> _p2s;
            readonly ImmutableArray<SolutionVersionInfo> _versions;
            readonly Dictionary<string, SVersion> _externalPackages;
            readonly Dictionary<string, SVersion> _externalDiscrepancies;

            public Mapping( Dictionary<string, Solution> p2s,
                                  ImmutableArray<SolutionVersionInfo> versions,
                                  Dictionary<string, SVersion> externalPackages,
                                  IReadOnlyDictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>> discrepancies )
            {
                _p2s = p2s;
                _versions = versions;
                _externalPackages = externalPackages;
                _externalDiscrepancies = discrepancies.ToDictionary( d => d.Key, d => d.Value.Max( c => c.Version )! );
            }

            public bool IsEmpty => _p2s.Count == 0 && _externalPackages.Count == 0;

            public SVersion? GetMappedVersion( string packageId, SVersion from )
            {
                return _p2s.TryGetValue( packageId, out var s )
                        ? _versions[s.OrderedIndex].LastBuild.Version
                        : _externalPackages.TryGetValue( packageId, out var result )
                            ? result
                            : _externalDiscrepancies.GetValueOrDefault( packageId );
            }

            public bool HasMapping( string packageId ) => _p2s.ContainsKey( packageId ) || _externalPackages.ContainsKey( packageId );
        }



    }
}
