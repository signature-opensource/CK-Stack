using CK.Core;
using CSemVer;
using Microsoft.VisualBasic;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace CKli.Build.Plugin;

sealed class PackageMapper : IPackageMapping
{
    Dictionary<string, VersionMap> _mapping;
    int _count;

    sealed class VersionMap : IPackageVersionMapping
    {
        readonly List<(SVersion From, SVersion To)> _map;

        public VersionMap( SVersion from, SVersion to )
        {
            _map = new List<(SVersion From, SVersion To)> { (from, to) };
        }

        public bool TryAdd( SVersion from, SVersion to )
        {
            var already = _map.FirstOrDefault( m => m.From == from );
            if( already.To != null )
            {
                return false;
            }
            _map.Add( (from, to) );
            return true;
        }

        public SVersion? Get( SVersion from ) => _map.FirstOrDefault( m => m.From == from ).To;

        public StringBuilder Write( StringBuilder b )
        {
            bool atLeastOne = false;
            foreach( var m in _map )
            {
                if( atLeastOne ) b.Append( ", " );
                atLeastOne = true;
                b.Append( m.From ).Append( " -> " ).Append( m.To ).AppendLine();
            }
            return b;
        }

        public override string ToString() => Write( new StringBuilder() ).ToString();

    }

    public PackageMapper()
    {
        _mapping = new Dictionary<string, VersionMap>();
    }

    public bool IsEmpty => _count != 0;

    public int Count => _count;

    public bool TryAdd( string packageId, SVersion from, SVersion to )
    {
        if( _mapping.TryGetValue( packageId, out var map ) )
        {
            if( !map.TryAdd( from, to ) )
            {
                return false;
            }
        }
        else
        {
            _mapping.Add( packageId, new VersionMap( from, to ) );
        }
        _count++;
        return true;
    }

    public void Add( string packageId, SVersion from, SVersion to )
    {
        if( !TryAdd( packageId, from, to ) )
        {
            Throw.CKException( $"Package '{packageId}' for version '{from}' is already mapped." );
        }
    }

    public bool TryGetMapping( string packageId, [NotNullWhen( true )] out IPackageVersionMapping? map )
    {
        if( _mapping.TryGetValue( packageId, out var cmap ) )
        {
            map = cmap;
            return true;
        }
        map = null;
        return false;
    }

    public SVersion? GetMappedVersion( string packageId, SVersion from )
    {
        return _mapping.TryGetValue( packageId, out var map )
                ? map.Get( from )
                : null;
    }

    public void Clear()
    {
        _mapping.Clear();
        _count = 0;
    }

    public StringBuilder Write( StringBuilder b )
    {
        foreach( var (name, map) in _mapping )
        {
            b.Append( name ).Append( ": " ).AppendLine();
            map.Write( b ).AppendLine();
        }
        return b;
    }

    public override string ToString() => Write( new StringBuilder() ).ToString();

}
