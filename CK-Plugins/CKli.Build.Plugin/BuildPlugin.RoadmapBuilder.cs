using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CSemVer;
using LibGit2Sharp;
using System.Collections.Generic;
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
        readonly bool? _runTest;
        readonly bool _singleBuild;

        public RoadmapBuilder( BuildPlugin buildPlugin, CKliEnv context, Roadmap roadmap, bool? runTest, int maxDoP )
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

        public async Task<bool> StartAsync( IActivityMonitor monitor )
        {
            Throw.DebugAssert( _roadmap.SolutionBuildCount > 0 );
            if( _singleBuild )
            {
                var s = _roadmap.Solutions.Single( s => s.MustBuild );
                Throw.DebugAssert( s.BuildInfo != null && !s.BuildInfo.DirectRequirements.Any( s => s.MustBuild ) );
                return await DoBuildAsync( monitor, s.BuildInfo, PackageMapper.Empty ) != null;
            }
            Queue<TaskCompletionSource<IActivityMonitor>>? waitingQueue = null;
            List<IActivityMonitor>? pool = null;
            int monitorCount = 0;
            _ = Task.Run( WaitForTermination );
            using( monitor.OpenInfo( $"Building {_roadmap.SolutionBuildCount} solutions." ) )
            {
                for(; ; )
                {
                    var msg = await _channel.Reader.ReadAsync();
                    if( msg is bool finalResult )
                    {
                        // There SHOULD never be any pending requests here: all tasks have been completed,
                        // they have released their monitor.
                        Throw.DebugAssert( waitingQueue == null || waitingQueue.Count == 0 );
                        return finalResult;
                    }
                    if( msg is TaskCompletionSource<IActivityMonitor> newMonitor )
                    {
                        if( monitorCount < _maxDoP )
                        {
                            newMonitor.SetResult( new ActivityMonitor( $"BuildMonitor n°{monitorCount}." ) );
                            monitorCount++;
                        }
                        else
                        {
                            Throw.DebugAssert( _roadmap.SolutionBuildCount > _maxDoP );
                            waitingQueue ??= new Queue<TaskCompletionSource<IActivityMonitor>>( _roadmap.SolutionBuildCount - _maxDoP );
                            waitingQueue.Enqueue( newMonitor );
                        }
                    }
                    else if( msg is IActivityMonitor released )
                    {
                        TODO
                    }
                }
            }
        }

        async Task WaitForTermination()
        {
            var buildTasks = new Task<bool>[_roadmap.SolutionBuildCount];
            BuildResult?[] req = await Task.WhenAll( _roadmap.Solutions.Where( s => s.MustBuild )
                                                                       .Select( s => s.BuildInfo!.BuildAsync( this ) )
                                                                       .ToArray() );
            _channel.Writer.TryWrite( req.Contains( null ) );
        }

        internal async Task<BuildResult?> BuildAsync( Roadmap.BuildInfo build, IPackageMapping mapping )
        {
            Throw.DebugAssert( !_singleBuild );
            // Acquires a monitor: a TaskCompletionSource is pushed.
            var monitorPromise = new TaskCompletionSource<IActivityMonitor>( TaskCreationOptions.RunContinuationsAsynchronously );
            _channel.Writer.TryWrite( monitorPromise );
            var monitor = await monitorPromise.Task;

            // Actual build.
            var result = await DoBuildAsync( monitor, build, mapping );

            // Returning the monitor.
            _channel.Writer.TryWrite( monitor );
            return result;
        }

        async Task<BuildResult?> DoBuildAsync( IActivityMonitor monitor, Roadmap.BuildInfo build, IPackageMapping mapping )
        {
            if( !EnsureAndCheckoutBranch( monitor, build.Solution, out var canAmend ) )
            {
                return null;
            }
            var commit = UpdateDependenciesAndCommit( monitor, build.Solution, mapping, canAmend );
            if( commit == null )
            {
                return null;
            }
            return await _buildPlugin.CoreBuildAsync( monitor, _context, build.VersionInfo, commit, build.TargetVersion, _runTest, forceRebuild: false );

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

            static Commit? UpdateDependenciesAndCommit( IActivityMonitor monitor, Roadmap.BuildSolution solution, IPackageMapping mapping, bool canAmend )
            {
                var s = MutableSolution.Create( monitor, solution.Repo );
                if( s == null )
                {
                    return null;
                }
                if( !s.UpdatePackages( monitor, mapping, null ) )
                {
                    return null;
                }
                var commitMsg = $"""
                Updating dependencies.

                {mapping.ToString()}
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
