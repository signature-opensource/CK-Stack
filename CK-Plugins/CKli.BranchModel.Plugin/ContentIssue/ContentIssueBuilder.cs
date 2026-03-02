using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Centralizes issue generation for the content of a repository: this handles the <see cref="HotBranch"/>
/// content and wraps multiple document related issues into one single Repo issue.
/// </summary>
public sealed class ContentIssueBuilder
{
    readonly BranchModelInfo _info;
    readonly Func<IActivityMonitor,Event, bool> _eventSender;
    List<ManualContentIssue>? _manualIssues;
    List<BaseDeleteIssue>? _deleteIssues;
    List<BaseMoveIssue>? _moveIssues;
    List<BaseEnsureFileIssue>? _ensureIssues;

    internal ContentIssueBuilder( BranchModelInfo info, Func<IActivityMonitor,Event,bool> eventSender )
    {
        _info = info;
        _eventSender = eventSender;
    }

    internal bool CreateIssue( IActivityMonitor monitor, out World.Issue? issue )
    {
        var ev = new Event( monitor, this );
        foreach( var b in _info.Branches )
        {
            if( b.BranchName.LinkType == BranchLinkType.None ) break;
            if( b.GitBranch == null ) continue;
            if( !_eventSender( monitor, ev ) )
            {
                // Stop on the first error.
                break;
            }
        }
        if( docIssues.Count > 0 )
        {
            issue = CreateIssue( monitor, docIssues );
            return issue != null;
        }
        issue = null;
        return true;

        static World.Issue? CreateIssue( IActivityMonitor monitor,  Dictionary<string, DocumentIssue> docIssues  )
        {
            Throw.DebugAssert( docIssues.Count > 0 );
            throw new NotImplementedException();
            return null;
        }
    }

    /// <summary>
    /// Raised by <see cref="BranchModelPlugin.ContentIssue"/> on "ckli issue" command when no branch related issues
    /// exist: this is used to check the content of the repositories (more precisely, the content of the
    /// <see cref="HotBranch"/> that has a <see cref="HotBranch.GitBranch"/>).
    /// </summary>
    public sealed class Event : EventMonitoredArgs
    {
        readonly ContentIssueBuilder _builder;
        HotBranch? _branch;
        INormalizedFileProvider? _content;

        internal Event( IActivityMonitor monitor, ContentIssueBuilder builder )
            : base( monitor )
        {
            _builder = builder;
        }

        internal void Initialize( HotBranch branch )
        {
            Throw.DebugAssert( branch.GitBranch != null );
            _branch = branch;
            _content = null;
        }

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _builder._info.Repo;

        /// <summary>
        /// Gets the hot branch that must be analyzed (<see cref="HotBranch.IsActive"/> is true).
        /// </summary>
        public HotBranch Branch => _branch!;

        /// <summary>
        /// Gets the non null <see cref="HotBranch.GitBranch"/> (because the branch is active).
        /// </summary>
        public Branch GitBranch => _branch!.GitBranch!;

        /// <summary>
        /// Gets the content branch: if it exists, it's the <see cref="HotBranch.GitDevBranch"/> otherwise
        /// the regular <see cref="HotBranch.GitBranch"/> is used.
        /// </summary>
        public Branch GitContentBranch => _branch!.GitDevBranch ?? _branch!.GitBranch!;

        /// <summary>
        /// Gets the content of the <see cref="Branch"/> from <see cref="GitContentBranch"/>.
        /// </summary>
        public INormalizedFileProvider Content => _content ??= _builder._info.ShallowSolutionPlugin.GetFiles( GitContentBranch.Tip );

        public IEnumerable<BaseDeleteIssue> GetDeleteAbove() => GetAbove( _builder._deleteIssues );

        IEnumerable<T> GetAbove<T>( List<T>? issues ) where T : ContentIssue 
        {
            if( issues != null )
            {
                foreach( var i in issues )
                {

                    yield return i;
                }
        }
    }

}

