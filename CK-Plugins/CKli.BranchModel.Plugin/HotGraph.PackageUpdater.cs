using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CSemVer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

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
        readonly Dictionary<string, SVersion> _externalPackages;
        readonly bool _buildRequired;
        Mapping? _packageMapping;

        internal PackageUpdater( HotGraph graph,
                                 ImmutableArray<SolutionVersionInfo> versions,
                                 bool buildRequired,
                                 Dictionary<string, SVersion> externalPackages )
        {
            _graph = graph;
            _versions = versions;
            _buildRequired = buildRequired;
            _externalPackages = externalPackages;
        }

        /// <summary>
        /// Gets the hot graph.
        /// </summary>
        public HotGraph Graph => _graph;

        /// <summary>
        /// Gets the package mapping with the current <see cref="SolutionVersionInfo.LastBuild"/> version of each solution
        /// even if <see cref="SolutionVersionInfo.VersionMustBuild"/> is true (the version can be a "+fake" or a "+deprecated").
        /// </summary>
        public IPackageMapping PackageMapping => _packageMapping ??= new Mapping( _graph._p2s, _versions, _externalPackages );

        /// <summary>
        /// Gets whether at least one <see cref="SolutionVersionInfo.VersionMustBuild"/> is true: the <see cref="PackageMapping"/> should not
        /// be used as-is, packages should be updated during a build, not directly.
        /// </summary>
        public bool BuildRequired => _buildRequired;

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

        sealed class Mapping( Dictionary<string, HotGraph.Solution> p2s,
                              ImmutableArray<HotGraph.SolutionVersionInfo> versions,
                              Dictionary<string, SVersion> externalPackages ) : IPackageMapping
        {
            public bool IsEmpty => p2s.Count == 0 && externalPackages.Count == 0;

            public SVersion? GetMappedVersion( string packageId, SVersion from )
            {
                return p2s.TryGetValue( packageId, out var s )
                        ? versions[s.OrderedIndex].LastBuild.Version
                        : externalPackages.GetValueOrDefault( packageId );
            }

            public bool HasMapping( string packageId ) => p2s.ContainsKey( packageId ) || externalPackages.ContainsKey( packageId );
        }

    }
}
