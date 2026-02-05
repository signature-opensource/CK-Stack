using CK.Core;
using LibGit2Sharp;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CKli.ShallowSolution.Plugin;

sealed class TreeFolder : IDirectoryContents, INormalizedFileProvider
{
    readonly Tree _t;

    public TreeFolder( Tree t )
    {
        _t = t;
    }

    bool IDirectoryContents.Exists => true;

    public DateTimeOffset LastModified => DateTimeOffset.MinValue;

    public IFileInfo? GetFileInfo( NormalizedPath sub )
    {
        var e = _t[sub];
        if( e != null && e.TargetType != TreeEntryTargetType.GitLink )
        {
            return new GitFileInfo( e, this );
        }
        return null;
    }

    public IDirectoryContents? GetDirectoryContents( NormalizedPath sub )
    {
        if( sub.IsEmptyPath ) return this;
        TreeEntry e = _t[sub];
        if( e != null && e.TargetType != TreeEntryTargetType.GitLink )
        {
            return new GitFileInfo( e, this );
        }
        return null;
    }

    public IEnumerator<IFileInfo> GetEnumerator()
    {
        return _t.Select( t => new GitFileInfo( t, this ) ).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

