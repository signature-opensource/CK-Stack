using CK.Core;
using Microsoft.Extensions.FileProviders;

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
        var f = _p.GetFileInfo( sub );
        return f.Exists ? f : null;
    }
}

