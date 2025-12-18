using CK.Core;
using CSemVer;
using Microsoft.VisualBasic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace CKli.ArtifactHandler.Plugin;

public sealed class BuildContentInfo : IEquatable<BuildContentInfo>
{
    readonly ImmutableArray<NuGetPackageInstance> _consumed;
    readonly ImmutableArray<string> _produced;
    readonly ImmutableArray<string> _assetFileNames;
    string? _toString;

    public BuildContentInfo( ImmutableArray<NuGetPackageInstance> consumed,
                             ImmutableArray<string> produced,
                             ImmutableArray<string> assetFileNames )
    {
        Throw.DebugAssert( !produced.Any( p => p.Contains( ' ' ) || p.Contains( ',' ) ) );
        Throw.DebugAssert( !assetFileNames.Any( p => p.Contains( ' ' ) || p.Contains( ',' ) ) );

        _consumed = consumed;
        _produced = produced;
        _assetFileNames = assetFileNames;
    }

    public BuildContentInfo( CKBinaryReader r )
    {
        var b = ImmutableArray.CreateBuilder<NuGetPackageInstance>( r.ReadNonNegativeSmallInt32() );
        for( int i = 0; i < b.Capacity; ++i )
        {
            b.Add( new NuGetPackageInstance( r.ReadString(), SVersion.Parse( r.ReadString() ) ) );
        }
        _consumed = b.MoveToImmutable();
        _produced = Read( r );
        _assetFileNames = Read( r );

        static ImmutableArray<string> Read( CKBinaryReader r )
        {
            int count = r.ReadNonNegativeSmallInt32();
            if( count > 0 )
            {
                var b = ImmutableArray.CreateBuilder<string>( count );
                for( int i = 0; i < count; ++i )
                {
                    b.Add( r.ReadString() );
                }
                return b.MoveToImmutable();
            }
            return [];
        }
    }

    public void Write( CKBinaryWriter w )
    {
        w.WriteNonNegativeSmallInt32( _consumed.Length );
        foreach( var p in _consumed )
        {
            w.Write( p.PackageId );
            w.Write( p.Version.ToString() );
        }
        Write( w, _produced );
        Write( w, _assetFileNames );

        static void Write( CKBinaryWriter w, ImmutableArray<string> a )
        {
            w.WriteNonNegativeSmallInt32( a.Length );
            foreach( var s in a )
            {
                w.Write( s ); 
            }
        }
    }

    /// <summary>
    /// Gets the consumed packages. These are sorted.
    /// </summary>
    public ImmutableArray<NuGetPackageInstance> Consumed => _consumed;

    /// <summary>
    /// Gets the produced packages identifiers. These are lexicographically sorted.
    /// </summary>
    public ImmutableArray<string> Produced => _produced;

    /// <summary>
    /// Gets the generated asset file names if any. These are lexicographically sorted.
    /// </summary>
    public ImmutableArray<string> AssetFileNames => _assetFileNames;

    /// <summary>
    /// Implements value equality semantics.
    /// </summary>
    /// <param name="other">The other content.</param>
    /// <returns>True if this content is the same as the other one.</returns>
    public bool Equals( BuildContentInfo? other )
    {
        if( ReferenceEquals( other, null ) ) return false;
        if( ReferenceEquals( other, this ) ) return true;
        return _consumed.SequenceEqual( other._consumed )
               && _produced.SequenceEqual( other._produced )
               && _assetFileNames.SequenceEqual( other._assetFileNames );
    }

    public override bool Equals( object? obj ) => Equals( obj as BuildContentInfo );

    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        foreach( var c in _consumed ) hash.Add( c.GetHashCode() );
        foreach( var p in _produced ) hash.Add( p.GetHashCode() );
        foreach( var a in _assetFileNames  ) hash.Add( a.GetHashCode() ); 
        return hash.ToHashCode();
    }

    /// <summary>
    /// Tries to parse the text content info.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="info">The resulting info on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( ReadOnlySpan<char> text, [NotNullWhen( true )] out BuildContentInfo? info )
    {
        if( text.TryMatchInteger<int>( out var consumedCount )
            && text.SkipWhiteSpaces()
            && text.TryMatch( "Consumed Packages:" )
            && text.SkipWhiteSpaces()
            && TryReadConsumedList( ref text, consumedCount, out var consumed )
            && text.SkipWhiteSpaces()
            && text.TryMatchInteger<int>( out var producedCount )
            && text.SkipWhiteSpaces()
            && text.TryMatch( "Produced Packages:" )
            && text.SkipWhiteSpaces()
            && TryReadStringList( ref text, producedCount, out var produced )
            && text.SkipWhiteSpaces()
            && text.TryMatchInteger<int>( out var assetsCount )
            && text.SkipWhiteSpaces()
            && text.TryMatch( "Asset Files:" )
            && text.SkipWhiteSpaces()
            && TryReadStringList( ref text, assetsCount, out var assets ) )
        {
            info = new BuildContentInfo( consumed, produced, assets );
            return true;
        }
        info = null;
        return false;

        static bool TryReadConsumedList( ref ReadOnlySpan<char> s, int count, out ImmutableArray<NuGetPackageInstance> packages )
        {
            if( count == 0 )
            {
                packages = [];
                return true;
            }
            NuGetPackageInstance previous = default;
            var b = ImmutableArray.CreateBuilder<NuGetPackageInstance>( count );
            while( NuGetPackageInstance.TryMatch( ref s, out var p ) )
            {
                if( previous.CompareTo( p ) >= 0 )
                {
                    packages = default;
                    return false;
                }
                b.Add( p );
                s.SkipWhiteSpaces();
                if( !s.TryMatch( ',' ) ) break;
                s.SkipWhiteSpaces();
            }
            if( b.Count == count )
            {
                packages = b.MoveToImmutable();
                return true;
            }
            packages = default;
            return false;
        }

        static bool TryReadStringList( ref ReadOnlySpan<char> s, int count, out ImmutableArray<string> strings )
        {
            if( count == 0 )
            {
                strings = [];
                return true;
            }
            string previous = string.Empty;
            var b = ImmutableArray.CreateBuilder<string>( count );
            while( TryReadString( ref s, out var text ) )
            {
                if( previous.CompareTo( text ) >= 0 )
                {
                    strings = default;
                    return false;
                }
                b.Add( text );
                s.SkipWhiteSpaces();
                if( !s.TryMatch( ',' ) ) break;
                s.SkipWhiteSpaces();
            }
            if( b.Count == count )
            {
                strings = b.MoveToImmutable();
                return true;
            }
            strings = default;
            return false;
        }

        static bool TryReadString( ref ReadOnlySpan<char> s, [NotNullWhen(true)]out string? text )
        {
            int i = s.IndexOfAny( "\r\n, " );
            if( i < 0 )
            {
                text = null;
                return false;
            }
            text = new string( s.Slice( 0, i ) );
            s = s.Slice( i );
            return true;
        }
    }

    /// <summary>
    /// Writes the <see cref="Consumed"/>, <see cref="Produced"/> and <see cref="AssetFileNames"/> as
    /// a text that can be parsed back by <see cref="TryParse(ReadOnlySpan{char}, out BuildContentInfo?)"/>.
    /// </summary>
    /// <param name="b"></param>
    /// <returns></returns>
    public StringBuilder Write( StringBuilder b )
    {
        if( _toString != null )
        {
            return b.Append( _toString );
        }
        var text = b.Append( _consumed.Length ).Append( " Consumed Packages: " )
                    .AppendJoin( ", ", _consumed ).AppendLine()
                    .Append( _produced.Length ).Append( " Produced Packages: " )
                    .AppendJoin( ", ", _produced ).AppendLine()
                    .Append( _assetFileNames.Length ).Append( " Asset Files: " )
                    .AppendJoin( ", ", _assetFileNames ).AppendLine();

        Throw.DebugAssert( TryParse( text.ToString(), out var clone )
                           && clone.Consumed.SequenceEqual( _consumed )
                           && clone.Produced.SequenceEqual( _produced )
                           && clone.AssetFileNames.SequenceEqual( _assetFileNames ) );

        return text;
    }

    /// <summary>
    /// Gets the text content info.
    /// </summary>
    /// <returns>The content info.</returns>
    public override string ToString() => _toString ??= Write( new StringBuilder() ).ToString();


    public static bool operator ==( BuildContentInfo? left, BuildContentInfo? right )
    {
        return EqualityComparer<BuildContentInfo>.Default.Equals( left, right );
    }

    public static bool operator !=( BuildContentInfo? left, BuildContentInfo? right )
    {
        return !(left == right);
    }
}
