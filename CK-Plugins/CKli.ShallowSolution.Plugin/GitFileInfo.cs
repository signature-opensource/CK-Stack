using CK.Core;
using LibGit2Sharp;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Internal: <see cref="IFileInfo"/> and <see cref="IDirectoryContents"/> surface.
/// </summary>
sealed class GitFileInfo : IFileInfo, IDirectoryContents
{
    readonly TreeEntry _e;
    readonly TreeFolder _c;

    internal GitFileInfo( TreeEntry e, TreeFolder c )
    {
        Debug.Assert( e.TargetType == TreeEntryTargetType.Blob || e.TargetType == TreeEntryTargetType.Tree );
        _e = e;
        _c = c;
    }

    Blob? Blob => _e.Target as Blob;

    public bool Exists => true;

    public long Length => Blob?.Size ?? -1;

    public string? PhysicalPath => null;

    public string Name => _e.Name;

    public DateTimeOffset LastModified => _c.LastModified;

    public bool IsDirectory => _e.TargetType == TreeEntryTargetType.Tree;

    public Stream CreateReadStream()
    {
        Throw.CheckState( !IsDirectory );
        Debug.Assert( Blob != null );
        return Blob.GetContentStream();
    }

    public IEnumerator<IFileInfo> GetEnumerator()
    {
        Throw.CheckState( IsDirectory );
        return ((Tree)_e.Target).Select( t => new GitFileInfo( t, _c ) ).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

