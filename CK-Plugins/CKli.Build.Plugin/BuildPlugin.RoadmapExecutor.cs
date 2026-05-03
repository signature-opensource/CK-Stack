using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    internal sealed class RoadmapExecutor
    {
        readonly Roadmap _roadmap;
        readonly BuildPlugin _buildPlugin;
        readonly Channel<object> _channel;
        readonly CKliEnv _context;
        readonly int _maxDoP;
        readonly bool? _runTest;
        readonly bool _singleBuild;

        public RoadmapExecutor( BuildPlugin buildPlugin,
                                CKliEnv context,
                                Roadmap roadmap,
                                bool? runTest,
                                int maxDoP )
        {
            Throw.DebugAssert( maxDoP >= 1 );
            Throw.DebugAssert( roadmap.SolutionBuildCount >= 1 );
            _roadmap = roadmap;
            _singleBuild = roadmap.SolutionBuildCount == 1;
            _runTest = runTest;
            _maxDoP = maxDoP;
            _buildPlugin = buildPlugin;
            _context = context;
            _channel = Channel.CreateUnbounded<object>( new UnboundedChannelOptions() { SingleReader = true } );
        }

        internal async Task<BuildResult[]?> BuildAsync( IActivityMonitor monitor )
        {
            Throw.DebugAssert( _roadmap.SolutionBuildCount > 0 );
            if( _singleBuild )
            {
                var s = _roadmap.OrderedSolutions.Single( s => s.MustBuild );
                Throw.DebugAssert( s.BuildInfo != null && !s.BuildInfo.DirectRequirements.Any( s => s.MustBuild ) );
                var r = await DoBuildAsync( monitor, s.BuildInfo );
                return r != null
                        ? s.BuildInfo.SetSingleBuildResult( r )
                        : null;
            }
            using( monitor.OpenInfo( $"Building {_roadmap.SolutionBuildCount} solutions (--max-dop {_maxDoP})." ) )
            {
                return await RunLoopAsync( monitor );
            }
        }

        async Task<BuildResult[]?> RunLoopAsync( IActivityMonitor monitor )
        {
            try
            {
                Queue<MonitorRequest>? waitingQueue = null;
                Queue<IActivityMonitor> monitorPool = new Queue<IActivityMonitor>( Math.Min( _maxDoP, 32 ) );
                int monitorCount = 0;
                _ = WaitForTermination();
                int remainingCount = _roadmap.SolutionBuildCount;
                for(; ; )
                {
                    var msg = await _channel.Reader.ReadAsync();
                    if( msg is BuildResult?[] results )
                    {
                        // There SHOULD never be any pending requests here: all tasks have been completed,
                        // they have released their monitor.
                        Throw.DebugAssert( waitingQueue == null || waitingQueue.Count == 0 );
                        while( monitorPool.TryDequeue( out var m ) )
                        {
                            m.MonitorEnd();
                        }
                        return (results.All( r => r != null ) ? results : null)!;
                    }
                    Throw.DebugAssert( msg is MonitorRequest );
                    var req = (MonitorRequest)msg;
                    if( req.MustAcquire )
                    {
                        if( monitorPool.TryDequeue( out var available ) )
                        {
                            req.SetMonitor( monitor, available );
                        }
                        else if( monitorCount < _maxDoP )
                        {
                            req.SetMonitor( monitor, new ActivityMonitor( $"Build Agent n°{++monitorCount}." ) );
                        }
                        else
                        {
                            Throw.DebugAssert( _roadmap.SolutionBuildCount > _maxDoP );
                            waitingQueue ??= new Queue<MonitorRequest>( _roadmap.SolutionBuildCount - _maxDoP );
                            waitingQueue.Enqueue( req );
                        }
                    }
                    else
                    {
                        --remainingCount;
                        if( req.BuildResult != null )
                        {
                            monitor.Info( ScreenType.CKliScreenTag, $"Build '{req.Build.Solution.Repo.DisplayPath}' succeed." );
                        }
                        else
                        {
                            monitor.Error( req.Message );
                        }
                        if( waitingQueue != null && waitingQueue.TryDequeue( out var waiter ) )
                        {
                            waiter.SetMonitor( monitor, req.Acquired );
                        }
                        else
                        {
                            monitorPool.Enqueue( req.Acquired );
                            Throw.DebugAssert( monitorPool.Count <= _maxDoP );
                        }
                    }
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"Unexpected error during roadmap execution.", ex );
                return null;
            }

        }

        async Task WaitForTermination()
        {
            var buildTasks = new Task<bool>[_roadmap.SolutionBuildCount];
            BuildResult?[] req = await Task.WhenAll( _roadmap.OrderedSolutions.Where( s => s.MustBuild )
                                                                       .Select( s => s.BuildInfo!.BuildAsync( this ) )
                                                                       .ToArray() );
            _channel.Writer.TryWrite( req );
        }

        sealed class MonitorRequest
        {
            readonly TaskCompletionSource<IActivityMonitor> _initialize;
            readonly ChannelWriter<object> _writer;
            readonly Roadmap.BuildInfo _build;
            string _message;
            BuildResult? _buildResult;

            public MonitorRequest( ChannelWriter<object> writer, Roadmap.BuildInfo build )
            {
                _initialize = new TaskCompletionSource<IActivityMonitor>( TaskCreationOptions.RunContinuationsAsynchronously );
                _writer = writer;
                _build = build;
                _message = $"Building roadmap n°{build.Solution.BuildNumber}/{build.Solution.Roadmap.SolutionBuildCount}: '{build.Solution.Repo.DisplayPath}'.";
                _writer.TryWrite( this );
            }

            public Roadmap.BuildInfo Build => _build;

            public Task<IActivityMonitor> AcquireAsync() => _initialize.Task;

            [MemberNotNullWhen( false, nameof( Acquired ) )]
            public bool MustAcquire => _initialize.Task.Status != TaskStatus.RanToCompletion;

            public IActivityMonitor? Acquired => _initialize.Task.Status != TaskStatus.RanToCompletion ? null : _initialize.Task.Result;

            public string Message => _message;

            public BuildResult? BuildResult => _buildResult;

            public void SetMonitor( IActivityMonitor monitor, IActivityMonitor available )
            {
                monitor.Info( _message );
                _initialize.SetResult( available );
            }

            public void Release( BuildResult? result )
            {
                _buildResult = result;
                if( result == null )
                {
                    _message = $"Failed to build '{_build.Solution.Repo.DisplayPath}'.";
                }
                _writer.TryWrite( this );
            }
        }

        internal async Task<BuildResult?> BuildAsync( Roadmap.BuildInfo buildInfo )
        {
            Throw.DebugAssert( !_singleBuild );
            // Acquires a monitor.
            var request = new MonitorRequest( _channel.Writer, buildInfo );
            var monitor = await request.AcquireAsync();

            // Actual build.
            BuildResult? result = null;
            try
            {
                result = await DoBuildAsync( monitor, buildInfo );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Error while building '{buildInfo.Solution}'.", ex );
            }
            // Returning the monitor to the pool (and handling centralized success/failure of builds).
            request.Release( result );
            return result;
        }

        // This doesn't catch exception. When called with a true _singleBuild, this is a unhandled
        // command exception handled at the root level.
        // When called in parallel, it is the BuildAsync wrapper above that handles it.
        async Task<BuildResult?> DoBuildAsync( IActivityMonitor monitor, Roadmap.BuildInfo build )
        {
            // EnsureAndCheckoutBranch and UpdateDependenciesAndCommit only interact with their own Repo:
            // parallel builds don't need synchronization for these.

            // This checks out the "dev/" (CI build) or integrate it in the regular branch and checks out the regular branch (non CI build).
            if( !EnsureAndCheckoutBranch( monitor, build, out var canAmend ) )
            {
                return null;
            }

            // This works on the working folder. (On success, UpdateDependenciesAndCommit refreshes our HotBranch). 
            var commit = UpdateDependenciesAndCommit( monitor, build, _roadmap.PackageMapping, canAmend );
            if( commit == null )
            {
                return null;
            }

            // CoreBuildAsync interacts with the ReleaseDatabasePlugin and this plugin is "thread safe" (thanks to a simple basic lock).
            // It also interacts with the ArtifactHandlerPlugin that is mainly a proxy of the file system (the $Local NuGet and Assets folders).
            var result = await _buildPlugin.CoreBuildAsync( monitor,
                                                            _context,
                                                            build.Solution.VersionInfo.VersionTagInfo,
                                                            commit,
                                                            build.TargetVersion,
                                                            _runTest,
                                                            forceRebuild: false ).ConfigureAwait( false );
            Throw.DebugAssert( result == null || result.Content.Produced.All( p => _roadmap.PackageMapping.GetMappedVersion( p, build.Solution.CurrentVersion ) == result.Version ) );
            // On error, we ensure that we let the repository on the "dev/" branch (this applies to non CI
            // build - in CI build as we already are on the "dev/" branch).
            if( result == null && !_roadmap.IsCIBuild )
            {
                // The files are exactly the same by design (hard reset has already been done by CoeBuild, no need to handle untracked & ignored files).
                build.Solution.Repo.GitRepository.Checkout( monitor, build.Solution.Solution.Branch.EnsureDevBranch(), deleteUntracked: false );
            }
            return result;

            static bool EnsureAndCheckoutBranch( IActivityMonitor monitor, Roadmap.BuildInfo build, out bool canAmend )
            {
                var b = build.Solution.Solution.Branch;
                Throw.DebugAssert( b.GitBranch != null );
                var gitRepository = build.Solution.Repo.GitRepository;
                Branch workingBranch;
                canAmend = false;
                bool hasDev = b.GitDevBranch != null;
                if( build.Solution.Roadmap.IsCIBuild )
                {
                    // CI build: easy, always work on the "dev/" branch.
                    workingBranch = b.EnsureDevBranch();
                }
                else
                {
                    // Stable build: if a "dev/" branch exists, integrate it.
                    if( b.GitDevBranch != null )
                    {
                        // Allow amend to update dependencies only if a merge commit
                        // has been created.
                        var before = b.GitDevBranch.Tip.Sha;
                        if( !b.IntegrateDevBranch( monitor ) )
                        {
                            return false;
                        }
                        // canAmend == a new merge commit has been created. 
                        canAmend = b.GitBranch.Tip.Sha != before;
                    }

                    workingBranch = b.GitBranch;
                }
                if( !gitRepository.Checkout( monitor, workingBranch ) )
                {
                    return false;
                }
                return true;
            }

            static Commit? UpdateDependenciesAndCommit( IActivityMonitor monitor,
                                                        Roadmap.BuildInfo build,
                                                        IPackageMapping mapping,
                                                        bool canAmend )
            {
                var repo = build.Solution.Repo;
                var s = MutableSolution.Create( monitor, repo );
                if( s == null )
                {
                    return null;
                }
                var mapped = new PackageMapper();
                if( !s.UpdatePackages( monitor, mapping, mapped ) )
                {
                    return null;
                }
                // Creates the commit... or not: this does nothing if there's nothing to do.
                // Here we create a commit on the regular branch if it has been integrated (non
                // CI build case).
                // This may not be a good idea and may change in the future.
                // We must refresh the HotBranch (because we don't use its Commit method).
                var commitMsg = $"""
                Updated dependencies.

                {mapped}
                """;

                GitRepository git = repo.GitRepository;
                if( git.Commit( monitor,
                                commitMsg,
                                canAmend
                                  ? CommitBehavior.AmendIfPossibleAndPrependPreviousMessage
                                  : CommitBehavior.CreateNewCommit ) == CommitResult.Error )
                {
                    return null;
                }

                //  We must check whether the commit to build has already produced another version
                //  (that SHOULD be a prerelease one). If it's the case, then we must create an empty
                //  commit to "carry" the stable version (this enables the VersionTagInfo to expose a
                //  simple TagCommitsBySha instead of a ListOfTagCommitsBySha).
                if( build.Solution.VersionInfo.VersionTagInfo.TagCommitsBySha.TryGetValue( git.Repository.Head.Tip.Sha, out var already ) )
                {
                    // We handle the case of a previous prerelease from the same commit. If we don't create
                    // an empty commit dedicated to the stable version, then the final check (in VersionTagInfo.CanBuildAnyCommit)
                    // triggers an error.
                    if( already.IsRegularVersion && already.Version.IsPrerelease )
                    {
                        if( git.Commit( monitor,
                                        $"Producing 'v{build.TargetVersion}' from unchanged '{already.Version.ParsedText}'.",
                                        CommitBehavior.CreateEmptyCommit ) == CommitResult.Error )
                        {
                            return null;
                        }
                        canAmend = true;
                    }
                    // If it's a +fake or a +deprecated, we let the final check trigger an error.
                }
                // Refreshing the HotBranch.
                if( !build.Solution.Solution.Branch.Refresh( monitor ) )
                {
                    return null;
                }
                return git.Repository.Head.Tip;
            }
        }
    }

}
