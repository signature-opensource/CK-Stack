using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CK.Core.CheckedWriteStream;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{

    [Description( """
        Local build the current Fix Workflow.
        """ )]
    [CommandPath( "fix build" )]
    public async Task<bool> FixBuildAsync( IActivityMonitor monitor,
                                           CKliEnv context,
                                           [Description( "Don't run tests even if they have never locally run on a commit." )]
                                           bool skipTests = false,
                                           [Description( "Run tests even if they have already run successfully on a commit." )]
                                           bool forceTests = false,
                                           [Description( "Force a rebuild." )]
                                           bool rebuild = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest )
            || !FixWorkflow.Load( monitor, World, out var workflow ) )
        {
            return false;
        }
        var results = await DoBuildFixAsync( monitor, context, runTest, workflow, rebuild, publishing: false ).ConfigureAwait( false );
        if( results.IsDefault )
        {
            return false;
        }
        context.Screen.Display( s => RenderBuildResults( s, results ) );
        return true;

    }

    [Description( """
        Local build the current Fix Workflow.
        """ )]
    [CommandPath( "fix publish" )]
    public async Task<bool> FixPublishAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             [Description( "Force a rebuild." )]
                                             bool rebuild = false )
    {
        if( !FixWorkflow.Load( monitor, World, out var workflow ) )
        {
            return false;
        }
        var results = await DoBuildFixAsync( monitor,
                                             context,
                                             runTest: rebuild ? true : null,
                                             workflow,
                                             rebuild,
                                             publishing: true ).ConfigureAwait( false );
        if( results.IsDefault )
        {
            return false;
        }
        context.Screen.Display( s => s.Text( "Publishing fix:" )
                                      .AddBelow( RenderBuildResults( s, results ) ) );
        return await _artifactHandler.PublishFixAsync( monitor, results );
    }

    static IRenderable RenderBuildResults( ScreenType s, ImmutableArray<BuildResult> results )
    {
        var d = s.Unit.AddBelow( results.Select( r => r.Repo.ToRenderable( s, withBranchName: true )
                                                            .AddRight( s.Text( r.Version.ToString() )
                                                                        .Box( marginLeft: 1,
                                                                                foreColor: r.SkippedBuild
                                                                                            ? ConsoleColor.DarkYellow
                                                                                            : ConsoleColor.Green ) ) ) );
        return d.TableLayout();
    }


    async Task<ImmutableArray<BuildResult>> DoBuildFixAsync( IActivityMonitor monitor,
                                                                CKliEnv context,
                                                                bool? runTest,
                                                                FixWorkflow? workflow,
                                                                bool rebuild,
                                                                bool publishing )
    {
        if( workflow == null )
        {
            monitor.Error( $"No current Fix Workflow exist for world '{World.Name}'." );
            return default;
        }
        if( !_branchModel.CheckBasicPreconditions( monitor, $"building '{workflow}'", out var allRepos ) )
        {
            return default;
        }
        var bResults = ImmutableArray.CreateBuilder<BuildResult>( workflow.Targets.Length );
        var packageMapper = new PackageMapper();
        var packageMapping = new FixPackageMapper( packageMapper );
        foreach( var target in workflow.Targets )
        {
            using( monitor.OpenInfo( $"Building n°{target.Index} - {target.Repo.DisplayPath}" ) )
            {
                if( !await BuildOneFixTarget( monitor,
                                              context,
                                              runTest,
                                              rebuild,
                                              publishing,
                                              bResults,
                                              packageMapper,
                                              packageMapping,
                                              target ).ConfigureAwait( false ) )
                {
                    break;
                }
            }
        }
        if( bResults.Count == workflow.Targets.Length )
        {
            var results = bResults.MoveToImmutable();

            var s = context.Screen.ScreenType;
            var display = RenderBuildResults( s, results );
            if( publishing ) display = s.Text( "Publishing fix:" ).AddBelow( display );
            context.Screen.Display( display );



            return results;
        }
        return default;
    }

    async Task<bool> BuildOneFixTarget( IActivityMonitor monitor,
                                        CKliEnv context,
                                        bool? runTest,
                                        bool rebuild,
                                        bool publishing,
                                        ImmutableArray<BuildResult>.Builder bResults,
                                        PackageMapper packageMapper,
                                        FixPackageMapper packageMapping,
                                        FixWorkflow.TargetRepo target )
    {
        var versionInfo = _versionTags.Get( monitor, target.Repo );

        var updated = new PackageMapper();
        if( !CheckoutFixTargetBranch( monitor, target, versionInfo, out var toFix, out int commitDepth )
            || !_solutionPlugin.UpdatePackages( monitor, target.Repo, packageMapping, updated )
            || !CommitUpdatedPackages( monitor, updated, target, out bool hasNewCommit ) )
        {
            return false;
        }
        Throw.DebugAssert( toFix.BuildContentInfo != null );

        if( hasNewCommit )
        {
            commitDepth++;
        }
        var targetVersion = target.TargetVersion;
        if( !publishing )
        {
            targetVersion = SVersion.Create( targetVersion.Major, targetVersion.Minor, targetVersion.Patch, $"local.fix.{commitDepth}" );
        }

        var result = await CoreBuildAsync( monitor,
                                            context,
                                            versionInfo,
                                            target.Repo.GitRepository.Repository.Head.Tip,
                                            targetVersion,
                                            runTest,
                                            forceRebuild: rebuild ).ConfigureAwait( false );
        if( result == null )
        {
            return false;
        }
        // We introduce a check here: we demand that the produced package identifiers are the same as the release
        // we are fixing: changing the produced packages that are structural/architectural artifacts is
        // everything but fixing.
        if( !result.SkippedBuild && !result.Content.Produced.SequenceEqual( toFix.BuildContentInfo.Produced ) )
        {
            monitor.Error( $"""
                    Forbidden change in produced packages for a fix in '{target.Repo.DisplayPath}':
                    The version 'v{target.ToFixVersion}' produced packages: '{toFix.BuildContentInfo.Produced.Concatenate( "', '" )}'.
                    But the new fix 'v{targetVersion}' produced: '{result.Content.Produced.Concatenate( "', '" )}'.
                    """ );
            _versionTags.DestroyLocalRelease( monitor, result.Repo, targetVersion );
            return false;
        }
        // Adds the new produced packages to the updates map.
        foreach( var p in result.Content.Produced )
        {
            packageMapper.Add( p, target.ToFixVersion, targetVersion );
        }
        Throw.DebugAssert( bResults.Count == target.Index );
        bResults.Add( result );
        return true;

        static bool CommitUpdatedPackages( IActivityMonitor monitor,
                                           PackageMapper? reusableUpdated,
                                           FixWorkflow.TargetRepo target,
                                           out bool hasNewCommit )
        {
            Throw.DebugAssert( reusableUpdated != null );
            hasNewCommit = false;
            if( !reusableUpdated.IsEmpty )
            {
                var b = new StringBuilder( "Updates: " );
                reusableUpdated.Write( b.AppendLine() );
                var commitResult = target.Repo.GitRepository.Commit( monitor, b.ToString() );
                if( commitResult is CommitResult.Error )
                {
                    return false;
                }
                hasNewCommit = commitResult is not CommitResult.NoChanges;
                reusableUpdated.Clear();
            }
            return true;
        }

        static bool CheckoutFixTargetBranch( IActivityMonitor monitor,
                                             FixWorkflow.TargetRepo target,
                                             VersionTagInfo versionInfo,
                                             [NotNullWhen( true )] out TagCommit? toFix,
                                             out int commitDepth )
        {
            commitDepth = 0;
            // We must be able to retrieve the TagCommit to fix.
            if( !versionInfo.TagCommits.TryGetValue( target.ToFixVersion, out toFix ) )
            {
                monitor.Error( $"Unable to find the commit '{target.ToFixCommitSha}' version 'v{target.ToFixVersion}' to be fixed in '{target.Repo.DisplayPath}'." );
                return false;
            }
            if( toFix.BuildContentInfo == null )
            {
                monitor.Error( $"The version 'v{target.ToFixVersion}' of the commit '{target.ToFixCommitSha}' to be fixed in '{target.Repo.DisplayPath}' has no more a valid build content." );
                return false;
            }

            GitRepository gitRepository = target.Repo.GitRepository;

            var branch = gitRepository.GetBranch( monitor, target.BranchName, CK.Core.LogLevel.Error );
            if( branch == null )
            {
                return false;
            }

            var divergence = gitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( toFix.Commit, branch.Tip );
            if( divergence.BehindBy == null )
            {
                monitor.Error( $"Branch '{target.BranchName}' in '{target.Repo.DisplayPath}' is not related to the commit '{target.ToFixCommitSha}' version 'v{target.ToFixVersion}' to be fixed." );
                return false;
            }
            commitDepth = divergence.BehindBy.Value;

            return gitRepository.Checkout( monitor, branch );
        }

    }

}
