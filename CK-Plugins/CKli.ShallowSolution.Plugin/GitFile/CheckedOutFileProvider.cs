using CK.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using System.IO;

namespace CKli.ShallowSolution.Plugin;

sealed class CheckedOutFileProvider : INormalizedFileProvider
{
    readonly PhysicalFileProvider _p;

    public CheckedOutFileProvider( string root )
    {
        _p = new PhysicalFileProvider( root );
    }

    public IDirectoryContents? GetDirectoryContents( NormalizedPath sub )
    {
        var c = _p.GetDirectoryContents( sub );
        return c.Exists ? c : null;
    }

    public IFileInfo? GetFileInfo( NormalizedPath sub )
    {
        // To be able to handle case issues here (on the file name), we
        // must match the Git (TreeFolder) behavior: the returned IFileInfo.Name
        // must be the store one (not the requested one).
        var fullPath = _p.Root + sub.Path;
        if( !File.Exists( fullPath ) ) return null;
        // The file exists but to get its exact case, we need to
        var dir = Path.GetDirectoryName( fullPath );
        var exact = Directory.GetFileSystemEntries( dir!, sub.LastPart )[0];
        // Reuse the PhysicalFileInfo class here... even with its rather useless
        // intermediate FileInfo.
        return new PhysicalFileInfo( new FileInfo( exact ) );
    }
}

