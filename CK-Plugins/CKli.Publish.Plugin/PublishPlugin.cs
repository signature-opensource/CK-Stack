using CK.Core;
using CKli.Core;
using System;
using CKli.Build.Plugin;
using System.Threading.Tasks;
using CKli.ArtifactHandler.Plugin;

namespace CKli.Publish.Plugin;

public sealed class PublishPlugin : PrimaryPluginBase
{
    readonly BuildPlugin _build;
    readonly ArtifactHandlerPlugin _artifactHandler;

    public PublishPlugin( PrimaryPluginContext primaryContext, BuildPlugin build, ArtifactHandlerPlugin artifactHandler )
        : base( primaryContext )
    {
        _build = build;
        _artifactHandler = artifactHandler;
        _build.OnRoadmapBuild.Async += OnRoadmapBuildAsync;
    }

    Task OnRoadmapBuildAsync( IActivityMonitor monitor, Roadmap.BuildEventArgs e, System.Threading.CancellationToken cancel )
    {
        return e.ShouldPublish ? PublishAsync( monitor, e.BuildDate, e.Roadmap ) : Task.CompletedTask;
    }

    Task<bool> PublishAsync( IActivityMonitor monitor, DateTime buildDate, Roadmap roadmap )
    {
        var packageSender = PackageSender.Create( monitor, roadmap.BranchName.Name, roadmap.IsCIBuild, _artifactHandler, World.StackRepository.SecretsStore );
        if( packageSender == null ) return Task.FromResult( false );

        var state = PublishState.Load( monitor, World );
        if( state == null ) return Task.FromResult( false );

        var newOne = WorldReleaseInfo.Create( buildDate, roadmap );
        state.Add( monitor, newOne );

        return state.RunAsync( monitor );
    }
}
