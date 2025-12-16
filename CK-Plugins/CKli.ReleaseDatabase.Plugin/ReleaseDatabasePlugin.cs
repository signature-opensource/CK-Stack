using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;

namespace CKli.ReleaseDatabase.Plugin;

public sealed class ReleaseDatabasePlugin : PrimaryPluginBase
{
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ReleaseDB _local;
    readonly ReleaseDB _published;

    public ReleaseDatabasePlugin( PrimaryPluginContext context, ArtifactHandlerPlugin artifactHandler )
        : base( context )
    {
        _artifactHandler = artifactHandler;
        var stackFolder = World.StackRepository.StackWorkingFolder;
        _local = new ReleaseDB( true, stackFolder.Combine( $"$Local/{World.Name.FullName}.LocalRelease.json" ) );
        _published = new ReleaseDB( false, stackFolder.Combine( $"{World.Name.FullName}.PublishedRelease.json" ) );
    }

    protected override bool Initialize( IActivityMonitor monitor )
    {
        return base.Initialize( monitor );
    }

    public bool OnLocalBuild( IActivityMonitor monitor, Repo repo, SVersion version, bool rebuild, BuildContentInfo content )
    {
        return _local.OnLocalBuild( monitor, repo, version, rebuild, content );
    }
}
