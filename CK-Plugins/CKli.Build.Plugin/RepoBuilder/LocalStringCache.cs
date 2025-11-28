using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.Build.Plugin;

/// <summary>
/// Very basic string cache stored in "<see cref="StackRepository.StackWorkingFolder"/>/$Local" folder.
/// </summary>
public sealed class LocalStringCache
{
    readonly StackRepository _stack;
    readonly string _name;
    string? _filePath;
    HashSet<string>? _cache;

    public LocalStringCache( StackRepository stack, string name )
    {
        _stack = stack;
        _name = name;
    }

    public bool Contains( IActivityMonitor monitor, string s ) => GetCache( monitor ).Contains( s );

    public void Add( IActivityMonitor monitor, string s )
    {
        var c = GetCache( monitor );
        c.Add( s );
        try
        {
            Throw.DebugAssert( _filePath != null );
            File.WriteAllLines( _filePath, c );
        }
        catch( Exception ex )
        {
            monitor.Warn( $"While writing '{_filePath}'.", ex );
        }
    }

    string GetFilePath() => _filePath ??= _stack.StackWorkingFolder.Combine( $"$Local/{_name}.txt" );

    HashSet<string> GetCache( IActivityMonitor monitor )
    {
        if( _cache == null )
        {
            var path = GetFilePath();
            try
            {
                _cache = new HashSet<string>( File.ReadLines( path ) );
            }
            catch( Exception ex )
            {
                monitor.Warn( $"While reading '{path}'.", ex );
                _cache = new HashSet<string>();
            }
        }
        return _cache;
    }
}
