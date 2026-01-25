using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CKli.Build.Plugin;

[DebuggerDisplay( "{ToString(),nq}" )]
sealed class PackageMapper : IPackageMapping
{
    readonly Dictionary<string, object> _mapping;

    public PackageMapper()
    {
        _mapping = new Dictionary<string, object>();
    }

    public bool IsEmpty => _mapping.Count == 0;

    public bool HasMapping( string packageId ) => _mapping.ContainsKey( packageId );

    public bool TryAdd( string packageId, SVersion from, SVersion to )
    {
        if( _mapping.TryGetValue( packageId, out var o ) )
        {
            if( o is Tuple<SVersion, SVersion> one )
            {
                _mapping[packageId] = new List<(SVersion, SVersion)> { (one.Item1, one.Item2), (from, to) };
            }
            else
            {
                var list = (List<(SVersion From, SVersion To)>)o;
                if( list.Any( m => m.From == from ) )
                {
                    return false;
                }
                list.Add( (from, to) );
            }
        }
        else
        {
            _mapping.Add( packageId, Tuple.Create( from, to ) );
        }
        return true;
    }

    public void Add( string packageId, SVersion from, SVersion to )
    {
        if( !TryAdd( packageId, from, to ) )
        {
            Throw.CKException( $"Package '{packageId}' for version '{from}' is already mapped." );
        }
    }

    public SVersion? GetMappedVersion( string packageId, SVersion from )
    {
        if( _mapping.TryGetValue( packageId, out var o ) )
        {
            if( o is Tuple<SVersion, SVersion> one )
            {
                if( one.Item1 == from ) return one.Item2;
            }
            else
            {
                var list = (List<(SVersion From, SVersion To)>)o;
                return list.FirstOrDefault( m => m.From == from ).To;
            }
        }
        return null;
    }

    public void Clear()
    {
        _mapping.Clear();
    }

    public StringBuilder Write( StringBuilder b )
    {
        foreach( var (name, o) in _mapping )
        {
            b.Append( name ).Append( ": " ).AppendLine();
            if( o is Tuple<SVersion, SVersion> one )
            {
                b.Append( one.Item1 ).Append( " -> " ).Append( one.Item2 ).AppendLine();
            }
            else
            {
                var list = (List<(SVersion From, SVersion To)>)o;
                foreach( var m in list )
                {
                    b.Append( m.From ).Append( " -> " ).Append( m.To ).AppendLine();
                }
            }
        }
        return b;
    }

    public override string ToString() => Write( new StringBuilder() ).ToString();
}
