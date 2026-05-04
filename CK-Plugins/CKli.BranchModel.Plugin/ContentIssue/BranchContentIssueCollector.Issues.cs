using CK.Core;
using CKli.Core;
using System;
using System.IO;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchContentIssueCollector
{
    abstract class BaseMoveIssue
    {
        readonly NormalizedPath _source;
        readonly NormalizedPath _target;
        readonly bool _fixingCase;

        private protected BaseMoveIssue( NormalizedPath source, NormalizedPath target )
        {
            Throw.DebugAssert( source != target );
            _source = source;
            _target = target;
            _fixingCase = source.Path.Equals( target.Path, StringComparison.OrdinalIgnoreCase );
        }

        public bool FixingCase => _fixingCase;

        public NormalizedPath Source => _source;

        public NormalizedPath Target => _target;

        public IRenderable ToRenderable( ScreenType screenType )
        {
            IRenderable r = screenType.Text( _source.Path ).Box( foreColor: ConsoleColor.Gray, marginRight: 1)
                                      .AddRight( screenType.Text( "→" ) )
                                      .AddRight( screenType.Text( _target ).Box( marginLeft: 1, foreColor: ConsoleColor.White ) );
            if( _fixingCase )
            {
                r = r.AddRight( screenType.Text( "(case differ)" ).Box( foreColor: ConsoleColor.Yellow, marginLeft: 1 ) );
            }
            return r;
        }


        public bool RegularMove( IActivityMonitor monitor, Repo repo )
        {
            Throw.DebugAssert( !_fixingCase );
            var source = repo.WorkingFolder.Combine( _source );
            var target = repo.WorkingFolder.Combine( _target );
            monitor.Trace( $"Moving: '{_source}' -> '{_target}'." );
            return Move( monitor, source, target );
        }

        public bool FixCase1( IActivityMonitor monitor, Repo repo )
        {
            Throw.DebugAssert( _fixingCase );
            var source = repo.WorkingFolder.Combine( _source );
            var sourceCase = source.Path + "[__CASING]";
            return Move( monitor, source, sourceCase );
        }

        public bool FixCase2( IActivityMonitor monitor, Repo repo )
        {
            Throw.DebugAssert( _fixingCase );
            var source = repo.WorkingFolder.Combine( _source );
            var sourceCase = source.Path + "[__CASING]";
            var target = repo.WorkingFolder.Combine( _target );
            monitor.Trace( $"Fixed case: '{_source}' -> '{_target}'." );
            return Move( monitor, sourceCase, target );
        }

        bool Move( IActivityMonitor monitor, string source, string target )
        {
            try
            {
                DoMove( source, target );
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While moving '{source}' to '{target}'.", ex );
                return false;
            }
        }

        protected abstract void DoMove( string source, string target );
    }

    sealed class MoveFileIssue : BaseMoveIssue
    {
        public MoveFileIssue( NormalizedPath source, NormalizedPath target )
            : base( source, target )
        {
        }

        protected override void DoMove( string source, string target )
        {
            File.Move( source, target );
        }
    }

    sealed class MoveFolderIssue : BaseMoveIssue
    {
        public MoveFolderIssue( NormalizedPath source, NormalizedPath target )
            : base( source, target )
        {
        }
        protected override void DoMove( string source, string target )
        {
            Directory.Move( source, target );
        }
    }

    abstract class BaseEnsureFileIssue
    {
        readonly NormalizedPath _path;
        readonly bool _create;

        public BaseEnsureFileIssue( NormalizedPath path, bool create )
        {
            _path = path;
            _create = create;
        }

        public NormalizedPath Path => _path;

        public bool Create => _create;

        internal bool Execute( IActivityMonitor monitor, Repo repo )
        {
            try
            {
                monitor.Trace( $"Updating file '{_path}'." );
                WriteFile( repo.WorkingFolder.Combine( _path ) );
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While updating '{_path}' content.", ex );
                return false;
            }
        }

        protected abstract void WriteFile( NormalizedPath path );
    }

    sealed class EnsureTextFileIssue : BaseEnsureFileIssue
    {
        readonly Func<string> _content;

        public EnsureTextFileIssue( NormalizedPath path, Func<string> content, bool create )
            : base( path, create )
        {
            _content = content;
        }

        protected override void WriteFile( NormalizedPath path ) => File.WriteAllText( path, _content() );
    }
}

