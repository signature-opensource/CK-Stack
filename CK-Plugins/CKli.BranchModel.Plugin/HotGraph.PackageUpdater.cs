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
    /// Exposes different <see cref="IPackageMapping"/> that must be used to update package dependencies in the <see cref="Graph"/>.
    /// <para>
    /// This is obtained by <see cref="HotGraph.GetPackageUpdater(IActivityMonitor)"/>. Requires that <see cref="VersionTagPlugin"/> has no issue.
    /// </para>
    /// </summary>
    public sealed class PackageUpdater
    {
        readonly HotGraph _graph;
        readonly ImmutableArray<SolutionVersionInfo> _versions;
        readonly bool _buildRequired;
        IReadOnlyDictionary<string, IReadOnlyList<(Solution Solution, SVersion Version)>>? _discrepancies;
        IPackageMapping? _alreadyBuiltMapping;
        IPackageMapping? _worldConfiguredMapping;
        IPackageMapping? _discrepanciesMapping;

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
        /// Gets whether at least one <see cref="SolutionVersionInfo.VersionMustBuild"/> is true.
        /// </summary>
        public bool BuildRequired => _buildRequired;

        /// <summary>
        /// Maps package name to one or more Solution in which their <see cref="Solution.ExternalDependencies"/> have different versions.
        /// <para>
        /// A solution may appear more than once if more than one of its project references different versions.
        /// </para>
        /// <para>
        /// This is computed on demand and cached and is the base for the <see cref="DiscrepanciesMapping"/>. 
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
        /// Gets the package mapping with the current <see cref="SolutionVersionInfo.LastBuild"/> version of each solution
        /// excluding true <see cref="SolutionVersionInfo.VersionMustBuild"/>: when the version is a "+fake" or a "+deprecated",
        /// mapping is ignored.
        /// </summary>
        public IPackageMapping AlreadyBuiltMapping => _alreadyBuiltMapping ??= new LastBuildVersionMapping( _graph._p2s, _versions );

        /// <summary>
        /// Gets the version mapping of the configured Worlds packages from the <see cref="VersionTag.Plugin.VersionTagPlugin.GetPackagesConfiguration(IActivityMonitor)"/>.
        /// </summary>
        public IPackageMapping WorldConfiguredMapping => _worldConfiguredMapping ??= BrutalPackageMapper.Create( _graph._externalPackages );

        /// <summary>
        /// Gets the version mapping that resolves <see cref="Discrepancies"/> by mapping to the greatest referenced version.
        /// </summary>
        public IPackageMapping DiscrepanciesMapping => _discrepanciesMapping ??= BrutalPackageMapper.Create( Discrepancies.ToDictionary( kv => kv.Key,
                                                                                                                                         kv => kv.Value.Max( sv => sv.Version )!,
                                                                                                                                         StringComparer.OrdinalIgnoreCase ) );

        sealed class LastBuildVersionMapping : IPackageMapping
        {
            readonly Dictionary<string, Solution> _p2s;
            readonly ImmutableArray<SolutionVersionInfo> _versions;

            public LastBuildVersionMapping( Dictionary<string, Solution> p2s, ImmutableArray<SolutionVersionInfo> versions )
            {
                _p2s = p2s;
                _versions = versions;
            }

            public bool IsEmpty => _p2s.Count == 0;

            public SVersion? GetMappedVersion( string packageId, SVersion from )
            {
                if( _p2s.TryGetValue( packageId, out var s ) )
                {
                    var sv = _versions[s.OrderedIndex];
                    return sv.VersionMustBuild
                            ? null
                            : _versions[s.OrderedIndex].LastBuild.Version;
                }
                return null;
            }

            public bool HasMapping( string packageId ) => _p2s.ContainsKey( packageId );
        }

    }
}
