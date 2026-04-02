using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

public sealed class PublishPlugin : PrimaryPluginBase
{
    readonly BuildPlugin _build;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ReleaseDatabasePlugin _releaseDatabase;

    public PublishPlugin( PrimaryPluginContext primaryContext,
                          BuildPlugin build,
                          ArtifactHandlerPlugin artifactHandler,
                          ReleaseDatabasePlugin releaseDatabase )
        : base( primaryContext )
    {
        _build = build;
        _artifactHandler = artifactHandler;
        _releaseDatabase = releaseDatabase;
        _build.OnRoadmapBuild.Async += OnRoadmapBuildAsync;
        _build.OnFixBuild.Async += OnFixBuildAsync;
    }

    async Task OnFixBuildAsync( IActivityMonitor monitor, FixBuildEventArgs e, CancellationToken cancel )
    {
        if( e.ShouldPublish )
        {
            if( !await PublishAsync( monitor, World, _artifactHandler, _releaseDatabase, e.BuildDate, e.FixWorkflow, e.Results, cancel ) )
            {
                e.SetFailed();
            }
        }

        static Task<bool> PublishAsync( IActivityMonitor monitor,
                                        World world,
                                        ArtifactHandlerPlugin artifactHandler,
                                        ReleaseDatabasePlugin releaseDatabase,
                                        DateTime buildDate,
                                        BranchModel.Plugin.FixWorkflow fixWorkflow,
                                        ImmutableArray<BuildResult> results,
                                        CancellationToken cancel )
        {
            // A fix is on the stable branch.
            var packageSender = PackageSender.Create( monitor, prereleaseName: "", ciBuild: false, artifactHandler, world.StackRepository.SecretsStore );
            if( packageSender == null ) return Task.FromResult( false );

            var state = PublishState.Load( monitor, world );
            if( state == null ) return Task.FromResult( false );

            var newOne = WorldReleaseInfo.Create( buildDate, fixWorkflow, results );
            state.Add( monitor, newOne );

            var publisher = new SimplePublisher( state, packageSender, releaseDatabase );
            return publisher.RunAsync( monitor, cancel );
        }
    }


    async Task OnRoadmapBuildAsync( IActivityMonitor monitor, RoadmapBuildEventArgs e, CancellationToken cancel )
    {
        if( e.ShouldPublish )
        {
            if( !await PublishAsync( monitor, World, _artifactHandler, _releaseDatabase, e.BuildDate, e.Roadmap, cancel ) )
            {
                e.SetFailed();
            }
        }

        static Task<bool> PublishAsync( IActivityMonitor monitor,
                                        World world,
                                        ArtifactHandlerPlugin artifactHandler,
                                        ReleaseDatabasePlugin releaseDatabase,
                                        DateTime buildDate,
                                        Roadmap roadmap,
                                        CancellationToken cancel )
        {
            var packageSender = PackageSender.Create( monitor, roadmap.Graph.BranchName.Name, roadmap.IsCIBuild, artifactHandler, world.StackRepository.SecretsStore );
            if( packageSender == null ) return Task.FromResult( false );

            var state = PublishState.Load( monitor, world );
            if( state == null ) return Task.FromResult( false );

            var newOne = WorldReleaseInfo.Create( buildDate, roadmap );
            state.Add( monitor, newOne );

            var publisher = new SimplePublisher( state, packageSender, releaseDatabase );
            return publisher.RunAsync( monitor, cancel );
        }
    }

}
