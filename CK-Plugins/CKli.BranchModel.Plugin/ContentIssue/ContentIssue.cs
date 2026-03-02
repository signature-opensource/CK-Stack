using CK.Core;
using CKli.Core;
using System;
using System.IO;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Base class for content issue in a <see cref="Branch"/>.
/// </summary>
public abstract class ContentIssue
{
    readonly HotBranch _branch;

    protected ContentIssue( HotBranch branch )
    {
        _branch = branch;
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _branch.Repo;

    /// <summary>
    /// Gets the branch that contains the content issue.
    /// </summary>
    public HotBranch Branch => _branch;

    ///// <summary>
    ///// Gets or sets whether the final commit can be amended (or a new commit must be created).
    ///// This defaults to true but is always false if <see cref="GitRepository.CanAmendCommit"/> is false.
    ///// </summary>
    //protected bool CanAmendCommit
    //{
    //    get => _canAmendCommit && _branch.Repo.GitRepository.CanAmendCommit;
    //    set => _canAmendCommit = value; 
    //}

    //internal protected virtual bool CheckoutDevBranch( IActivityMonitor monitor, CKliEnv context )
    //{
    //    Throw.DebugAssert( !_isManual );
    //    return _branch.CheckoutDevBranch( monitor, context );
    //}

    //internal bool Execute( IActivityMonitor monitor, CKliEnv context )
    //{
    //}

    //internal protected virtual bool FinalCommit( IActivityMonitor monitor, CKliEnv context )
    //{
    //    return _branch.Repo.GitRepository..CheckoutDevBranch( monitor, context );

    //}
}

public sealed class ManualContentIssue : ContentIssue
{
    readonly string _description;

    public ManualContentIssue( HotBranch branch, string description )
        : base( branch )
    {
        _description = description;
    }

    /// <summary>
    /// Gets the description of this manual issue.
    /// </summary>
    public string Description => _description;
}

public abstract class BaseDeleteIssue : ContentIssue
{
    readonly NormalizedPath _path;

    private protected BaseDeleteIssue( HotBranch branch, NormalizedPath path )
        : base( branch )
    {
        _path = Repo.WorkingFolder.Combine( path );
    }

    public NormalizedPath Path => _path;

    private protected abstract bool Execute( IActivityMonitor monitor );
}

public sealed class DeleteFileIssue : BaseDeleteIssue
{
    public DeleteFileIssue( HotBranch branch, NormalizedPath path )
        : base( branch, path )
    {
    }

    private protected override bool Execute( IActivityMonitor monitor ) => FileHelper.DeleteFile( monitor, Path );
}

public sealed class DeleteFolderIssue : BaseDeleteIssue
{
    public DeleteFolderIssue( HotBranch branch, NormalizedPath path )
        : base( branch, path )
    {
    }
    private protected override bool Execute( IActivityMonitor monitor ) => FileHelper.DeleteFolder( monitor, Path );
}

public abstract class BaseMoveIssue : ContentIssue
{
    readonly NormalizedPath _source;
    readonly NormalizedPath _target;
    readonly bool _fixingCase;

    private protected BaseMoveIssue( HotBranch branch, NormalizedPath source, NormalizedPath target )
        : base( branch )
    {
        Throw.CheckArgument( source != target );
        _source = Repo.WorkingFolder.Combine( source );
        _target = Repo.WorkingFolder.Combine( target );
        _fixingCase = _source.Path.Equals( _target.Path, StringComparison.OrdinalIgnoreCase );
    }

    public bool FixingCase => _fixingCase;
}

public sealed class MoveFileIssue : BaseMoveIssue
{
    public MoveFileIssue( HotBranch branch, NormalizedPath source, NormalizedPath target )
        : base( branch, source, target )
    {
    }
}

public sealed class MoveFolderIssue : BaseMoveIssue
{
    public MoveFolderIssue( HotBranch branch, NormalizedPath source, NormalizedPath target )
        : base( branch, source, target )
    {
    }
}

public abstract class BaseEnsureFileIssue : ContentIssue
{
    readonly NormalizedPath _path;

    public BaseEnsureFileIssue( HotBranch branch, NormalizedPath path )
        : base( branch )
    {
        _path = Repo.WorkingFolder.Combine( path ); ;
    }

    internal bool Execute( IActivityMonitor monitor )
    {
        try
        {
            UpdateFile( _path );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While updating '{_path}' content.", ex );
            return false;
        }
    }

    protected abstract void UpdateFile( NormalizedPath path );
}

public sealed class EnsureTextFileIssue : BaseEnsureFileIssue
{
    readonly Func<string> _content;

    public EnsureTextFileIssue( HotBranch branch, NormalizedPath path, Func<string> content )
        : base( branch, path )
    {
        _content = content;
    }

    protected override void UpdateFile( NormalizedPath path ) => File.WriteAllText( path, _content() );
}

