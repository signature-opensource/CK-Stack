using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchContentIssue
{
    readonly HotBranch _branch;
    readonly BranchContentIssue? _previous;
    List<string>? _manual;
    List<NormalizedPath>? _deleteFiles;
    List<NormalizedPath>? _deleteFolders;
    List<MoveFileIssue>? _moveFiles;
    List<MoveFolderIssue>? _moveFolders;
    List<BaseEnsureFileIssue>? _createFiles;
    List<BaseEnsureFileIssue>? _updateFiles;
    int _issueCount;

    public BranchContentIssue( HotBranch branch, BranchContentIssue? previous )
    {
        _branch = branch;
        _previous = previous;
    }

    public HotBranch Branch => _branch;

    /// <summary>
    /// Gets the count of recorded (non manual) issues.
    /// </summary>
    public int AutoCount => _issueCount;

    /// <summary>
    /// Gets the count of manual issues.
    /// </summary>
    public int ManualCount => _manual == null ? 0 : _manual.Count;

    public void ManualFix( string description )
    {
        _manual ??= new List<string>();
        _manual.Add( description );
    }

    public void DeleteFile( NormalizedPath path )
    {
        _deleteFiles ??= new List<NormalizedPath>();
        _deleteFiles.Add( path );
        ++_issueCount;
    }

    public void DeleteFolder( NormalizedPath path )
    {
        _deleteFolders ??= new List<NormalizedPath>();
        _deleteFolders.Add( path );
        ++_issueCount;
    }

    public void MoveFile( NormalizedPath source, NormalizedPath target )
    {
        Throw.CheckArgument( source != target );
        _moveFiles ??= new List<MoveFileIssue>();
        _moveFiles.Add( new MoveFileIssue( source, target ) );
        ++_issueCount;
    }

    public void MoveFolder( NormalizedPath source, NormalizedPath target )
    {
        Throw.CheckArgument( source != target );
        _moveFolders ??= new List<MoveFolderIssue>();
        _moveFolders.Add( new MoveFolderIssue( source, target ) );
        ++_issueCount;
    }

    public void CreateFile( NormalizedPath path, Func<string> content )
    {
        _createFiles ??= new List<BaseEnsureFileIssue>();
        _createFiles.Add( new EnsureTextFileIssue( path, content, create: true ) );
        ++_issueCount;
    }

    public void UpdateFile( NormalizedPath path, Func<string> content )
    {
        _updateFiles ??= new List<BaseEnsureFileIssue>();
        _updateFiles.Add( new EnsureTextFileIssue( path, content, create: false ) );
        ++_issueCount;
    }

    internal IRenderable AppendManualDescription( IRenderable r )
    {
        return _manual != null
                    ? r.AddBelow( _manual.Select( d => r.ScreenType.Text( $"- {d}" ) ) )
                    : r;
    }

    internal IRenderable AppendBranchDescription( IRenderable r )
    {
        Throw.DebugAssert( AutoCount > 0 );
        var h = r.ScreenType.Text( "Branch:" ).Box( marginRight:1 )
                            .AddRight( r.ScreenType.Text( _branch.BranchName.Name ).Box( foreColor: ConsoleColor.Magenta, marginRight: 1 ) )
                            .AddRight( r.ScreenType.Text( AutoCount == 1 ? "(1 content issue)" : $"({AutoCount} content issues)" ) );
        if( _deleteFiles != null )
        {
            h = RenderDelete( h, _deleteFiles, "ile" );
        }
        if( _deleteFolders != null )
        {
            h = RenderDelete( h, _deleteFolders, "older" );
        }

        static IRenderable RenderDelete( IRenderable h, List<NormalizedPath> paths, string what )
        {
            Throw.DebugAssert( paths.Count > 0 );
            if( paths.Count == 1 )
            {
                h = h.AddBelow( h.ScreenType.Text( $"> F{what} '{paths[0]}' must be deleted." ) );
            }
            else
            {
                h = h.AddBelow( h.ScreenType.Text( $"""
                    > {paths.Count} f{what}s must be deleted:
                    {paths.Select( p => $"- {p}" ).Concatenate( Environment.NewLine )}'.
                    """ ) );
            }
            return h;
        }

        if( _moveFiles != null )
        {
            h = RenderMove( h, _moveFiles, "ile", _moveFiles.Count );
        }
        if( _moveFolders != null )
        {
            h = RenderMove( h, _moveFolders, "older", _moveFolders.Count );
        }

        static IRenderable RenderMove( IRenderable h, IEnumerable<BaseMoveIssue> moves, string what, int count )
        {
            Throw.DebugAssert( count > 0 );
            if( count == 1 )
            {

                h = h.AddBelow( h.ScreenType.Text( $"> F{what} must be moved:" ).Box( marginRight: 1 )
                     .AddRight( moves.First().ToRenderable( h.ScreenType ) ) );
            }
            else
            {
                h = h.AddBelow( h.ScreenType.Text( $"> {count} f{what}s must be moved:" ) )
                     .AddBelow( moves.Select( m => h.ScreenType.Text( "-" ).Box( marginRight: 1 )
                                                    .AddRight( m.ToRenderable( h.ScreenType ) ) ) );
            }
            return new Collapsable( h );
        }

        if( _createFiles != null )
        {
            if( _createFiles.Count == 1 )
            {
                h = h.AddBelow( h.ScreenType.Text( $"> File '{_createFiles[0].Path}' must be created." ) );
            }
            else
            {
                h = h.AddBelow( h.ScreenType.Text( $"""
                    > {_createFiles.Count} files must be created:
                    {_createFiles.Select( p => $"- {p.Path}" ).Concatenate( Environment.NewLine )}'.
                    """ ) );
            }
        }

        if( _updateFiles != null )
        {
            if( _updateFiles.Count == 1 )
            {
                h = h.AddBelow( h.ScreenType.Text( $"> File '{_updateFiles[0].Path}' must be updated." ) );
            }
            else
            {
                h = h.AddBelow( h.ScreenType.Text( $"""
                    > {_updateFiles.Count} files must be updated:
                    {_updateFiles.Select( p => $"- {p.Path}" ).Concatenate( Environment.NewLine )}'.
                    """ ) );
            }
        }

        return h;
    }

    internal bool Execute( IActivityMonitor monitor, CKliEnv context, Repo repo, IRenderable body )
    {
        // First handles case fix (with an intermediate commit).
        IEnumerable<BaseMoveIssue> moves = (IEnumerable<BaseMoveIssue>?)_moveFiles ?? [];
        if( _moveFolders != null ) moves = moves.Concat( _moveFolders );

        bool success = ApplyFixCase( monitor, repo, moves, out int regularMoveCount );

        if( success )
        {
            if( regularMoveCount > 0 )
            {
                foreach( var m in moves )
                {
                    if( !m.FixingCase )
                    {
                        success &= m.RegularMove( monitor, repo );
                    }
                }
            }
            if( success )
            {
                if( _deleteFiles != null )
                {
                    foreach( var p in _deleteFiles )
                    {
                        success &= FileHelper.DeleteFile( monitor, repo.WorkingFolder.Combine( p ) );
                    }
                }
                if( success && _deleteFolders != null )
                {
                    foreach( var p in _deleteFolders )
                    {
                        success &= FileHelper.DeleteFolder( monitor, repo.WorkingFolder.Combine( p ) );
                    }
                }
                if( success && _createFiles != null )
                {
                    foreach( var c in _createFiles )
                    {
                        success &= c.Execute( monitor, repo );
                    }
                }
                if( success && _updateFiles != null )
                {
                    foreach( var c in _updateFiles )
                    {
                        success &= c.Execute( monitor, repo );
                    }
                }
            }
        }
        return success;

        static bool ApplyFixCase( IActivityMonitor monitor, Repo repo, IEnumerable<BaseMoveIssue> moves, out int regularMoveCount )
        {
            bool success = true;
            int caseFixCount = 0;
            regularMoveCount = 0;
            foreach( var m in moves )
            {
                if( m.FixingCase )
                {
                    ++caseFixCount;
                    success &= m.FixCase1( monitor, repo );
                }
                else
                {
                    ++regularMoveCount;
                }
            }
            if( success && caseFixCount > 0 )
            {
                if( repo.GitRepository.Commit( monitor, $"Fixing {caseFixCount} case differences." ) != CommitResult.Error )
                {
                    foreach( var m in moves )
                    {
                        if( m.FixingCase )
                        {
                            success &= m.FixCase2( monitor, repo );
                        }
                    }
                }
                else
                {
                    success = false;
                }
            }

            return success;
        }

    }
}

