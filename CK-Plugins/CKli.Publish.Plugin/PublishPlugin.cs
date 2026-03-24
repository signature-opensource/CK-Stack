using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CKli.Build.Plugin;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

public sealed class PublishPlugin : PrimaryPluginBase
{
    readonly BuildPlugin _build;

    public PublishPlugin( PrimaryPluginContext primaryContext, BuildPlugin build )
        : base( primaryContext )
    {
        _build = build;
        _build.OnRoadmapBuild.Async += OnRoadmapBuildAsync;
    }

    Task OnRoadmapBuildAsync( IActivityMonitor monitor, Roadmap.BuildEventArgs e, System.Threading.CancellationToken cancel )
    {
        return e.ShouldPublish ? PublishAsync( monitor, e.Roadmap ) : Task.CompletedTask;
    }

    async Task PublishAsync( IActivityMonitor monitor, Roadmap roadmap )
    {
        roadmap.OrderedSolutions.Where( s => s.MustBuild ).Select( s => (s.Repo, s.BuildInfo!.TargetVersion, s.VersionInfo.CommitsFromBaseBuild) );
    }
}
