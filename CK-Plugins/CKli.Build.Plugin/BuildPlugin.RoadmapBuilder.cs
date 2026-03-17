using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    internal sealed class RoadmapBuilder
    {
        readonly Roadmap _roadmap;
        readonly BuildPlugin _buildPlugin;
        readonly Channel<object> _channel;
        readonly CKliEnv _context;
        readonly int _maxDoP;
        readonly ConcurrentDictionary<string, SVersion> _mapping;
        readonly IPackageMapping _packageMapping;
        readonly bool? _runTest;
        readonly bool _singleBuild;

        public RoadmapBuilder( BuildPlugin buildPlugin,
                               CKliEnv context,
                               Roadmap roadmap,
                               bool? runTest,
                               int maxDoP,
                               ConcurrentDictionary<string, SVersion> mapping )
        {
            Throw.DebugAssert( maxDoP >= 1 );
            Throw.DebugAssert( roadmap.SolutionBuildCount >= 1 );
            _roadmap = roadmap;
            _singleBuild = roadmap.SolutionBuildCount == 1;
            _runTest = runTest;
            _maxDoP = maxDoP;
            _mapping = mapping;
            _packageMapping = BrutalPackageMapper.Create( mapping );
            _buildPlugin = buildPlugin;
            _context = context;
            _channel = Channel.CreateUnbounded<object>( new UnboundedChannelOptions() { SingleReader = true } );
        }

        internal async Task<bool> BuildAsync( IActivityMonitor monitor )
        {
            Throw.DebugAssert( _roadmap.SolutionBuildCount > 0 );
            if( _singleBuild )
            {
                var s = _roadmap.OrderedSolutions.Single( s => s.MustBuild );
                Throw.DebugAssert( s.BuildInfo != null && !s.BuildInfo.DirectRequirements.Any( s => s.MustBuild ) );
                return await DoBuildAsync( monitor, s.BuildInfo ) != null;
            }
            using( monitor.OpenInfo( $"Building {_roadmap.SolutionBuildCount} solutions." ) )
            {
                return await RunLoopAsync( monitor );
            }
        }

        async Task<bool> RunLoopAsync( IActivityMonitor monitor )
        {
            Queue<MonitorRequest>? waitingQueue = null;
            Queue<IActivityMonitor> pool = new Queue<IActivityMonitor>( Math.Min( _maxDoP, 32 ) );
            int monitorCount = 0;
            _ = Task.Run( WaitForTermination );
            int remainingCount = _roadmap.SolutionBuildCount;
            bool hasError = false;
            for(; ; )
            {
                var msg = await _channel.Reader.ReadAsync();
                if( msg is bool finalResult )
                {
                    // There SHOULD never be any pending requests here: all tasks have been completed,
                    // they have released their monitor.
                    Throw.DebugAssert( waitingQueue == null || waitingQueue.Count == 0 );
                    while( pool.TryDequeue( out var m ) )
                    {
                        m.MonitorEnd();
                    }
                    return finalResult;
                }
                Throw.DebugAssert( msg is MonitorRequest );
                var req = (MonitorRequest)msg;
                if( req.MustAcquire )
                {
                    if( pool.TryDequeue( out var available ) )
                    {
                        req.SetMonitor( monitor, available );
                    }
                    else if( monitorCount < _maxDoP )
                    {
                        req.SetMonitor( monitor, new ActivityMonitor( $"Build Agent n°{monitorCount}." ) );
                        monitorCount++;
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
                    if( req.Success )
                    {
                        if( !hasError )
                        {
                            --remainingCount;
                            monitor.Info( $"Build '{req.Build.Solution.Repo.DisplayPath}' success, {remainingCount} remaining out of {_roadmap.SolutionBuildCount}." );
                        }
                    }
                    else
                    {
                        hasError = true;
                        monitor.Error( req.Message );
                    }
                    if( waitingQueue != null && waitingQueue.TryDequeue( out var waiter ) )
                    {
                        waiter.SetMonitor( monitor, req.Acquired );
                    }
                    else
                    {
                        pool.Enqueue( req.Acquired );
                        Throw.DebugAssert( pool.Count <= _maxDoP );
                    }
                }
            }
        }

        async Task WaitForTermination()
        {
            var buildTasks = new Task<bool>[_roadmap.SolutionBuildCount];
            BuildResult?[] req = await Task.WhenAll( _roadmap.OrderedSolutions.Where( s => s.MustBuild )
                                                                       .Select( s => s.BuildInfo!.BuildAsync( this ) )
                                                                       .ToArray() );
            _channel.Writer.TryWrite( !req.Contains( null ) );
        }

        sealed class MonitorRequest
        {
            readonly TaskCompletionSource<IActivityMonitor> _initialize;
            readonly TaskCompletionSource _finalize;
            readonly ChannelWriter<object> _writer;
            readonly Roadmap.BuildInfo _build;
            string _message;
            BuildResult? _buildResult;

            public MonitorRequest( ChannelWriter<object> writer, Roadmap.BuildInfo build )
            {
                _initialize = new TaskCompletionSource<IActivityMonitor>( TaskCreationOptions.RunContinuationsAsynchronously );
                _finalize = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
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

            public bool Success => _buildResult != null;

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
            var result = await DoBuildAsync( monitor, buildInfo );

            // Returning the monitor to the pool (and handling centralized success/failure of builds).
            request.Release( result );
            return result;
        }

        async Task<BuildResult?> DoBuildAsync( IActivityMonitor monitor, Roadmap.BuildInfo build )
        {
            if( !EnsureAndCheckoutBranch( monitor, build.Solution, out var canAmend ) )
            {
                return null;
            }
            var commit = UpdateDependenciesAndCommit( monitor, build.Solution, _packageMapping, canAmend );
            if( commit == null )
            {
                return null;
            }
            var result = await _buildPlugin.CoreBuildAsync( monitor, _context, build.VersionInfo, commit, build.TargetVersion, _runTest, forceRebuild: false );
            if( result != null )
            {
                foreach( var p in result.Content.Produced )
                {
                    _mapping.TryAdd( p, result.Version );
                }
            }
            return result;

            static bool EnsureAndCheckoutBranch( IActivityMonitor monitor, Roadmap.BuildSolution solution, out bool canAmend )
            {
                var b = solution.Solution.Branch;
                Throw.DebugAssert( b.GitBranch != null );
                Branch workingBranch;
                canAmend = false;
                bool hasDev = b.GitDevBranch != null;
                if( solution.Roadmap.IsDevBuild )
                {
                    if( b.GitDevBranch == null )
                    {
                        workingBranch = b.EnsureDevBranch();
                        canAmend = true;
                    }
                    else
                    {
                        workingBranch = b.GitDevBranch;
                    }
                }
                else
                {
                    if( hasDev && !b.IntegrateDevBranch( monitor ) )
                    {
                        return false;
                    }
                    workingBranch = b.GitBranch;
                    canAmend = true;
                }
                if( !solution.Repo.GitRepository.Checkout( monitor, workingBranch ) )
                {
                    return false;
                }
                return true;
            }

            static Commit? UpdateDependenciesAndCommit( IActivityMonitor monitor,
                                                        Roadmap.BuildSolution solution,
                                                        IPackageMapping mapping,
                                                        bool canAmend )
            {
                var s = MutableSolution.Create( monitor, solution.Repo );
                if( s == null )
                {
                    return null;
                }
                var mapped = new PackageMapper();
                if( !s.UpdatePackages( monitor, mapping, mapped ) )
                {
                    return null;
                }
                var commitMsg = $"""
                Updated dependencies.

                {mapped.ToString()}
                """;
                return solution.Repo.GitRepository.Commit( monitor,
                                                           commitMsg,
                                                           canAmend
                                                              ? CommitBehavior.AmendIfPossibleAndPrependPreviousMessage
                                                              : CommitBehavior.CreateNewCommit ) != CommitResult.Error
                        ? solution.Repo.GitRepository.Repository.Head.Tip
                        : null;
            }
        }
    }

}
