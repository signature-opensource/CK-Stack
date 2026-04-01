using CKli.BranchModel.Plugin;
using CKli.ShallowSolution.Plugin;
using CSemVer;
using System.Collections.Immutable;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    sealed class Mapping : IPackageMapping
    {
        readonly HotGraph.PackageUpdater _packageUpdater;
        readonly ImmutableArray<BuildSolution> _orderedSolutions;

        public Mapping( HotGraph.PackageUpdater packageUpdater, ImmutableArray<BuildSolution> orderedSolutions )
        {
            _packageUpdater = packageUpdater;
            _orderedSolutions = orderedSolutions;
        }

        public bool IsEmpty => false;

        public SVersion? GetMappedVersion( string packageId, SVersion from )
        {
            // Packages produced by this World are fully handled here: this lookup handles current target build
            // versions and already built versions (it's useless to lookup the _packageUpdater.AlreadyBuiltMapping mappings).
            if( _packageUpdater.Graph.ProducedPackages.TryGetValue( packageId, out var localSolution ) )
            {
                var b = _orderedSolutions[localSolution.OrderedIndex];
                if( b.MustBuild )
                {
                    return b.BuildInfo.TargetVersion;
                }
                return b.VersionInfo.VersionMustBuild
                        ? null
                        : b.VersionInfo.LastBuild.Version;
            }
            return _packageUpdater.WorldConfiguredMapping.GetMappedVersion( packageId, from )
                    ?? _packageUpdater.DiscrepanciesMapping.GetMappedVersion( packageId, from );
        }

        public bool HasMapping( string packageId ) => _packageUpdater.Graph.ProducedPackages.ContainsKey( packageId )
                                                      || _packageUpdater.WorldConfiguredMapping.HasMapping( packageId )
                                                      || _packageUpdater.DiscrepanciesMapping.HasMapping( packageId );
    }

}
