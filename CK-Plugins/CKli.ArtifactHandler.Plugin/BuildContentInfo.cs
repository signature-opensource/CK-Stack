using CK.Core;
using System;
using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace CKli.ArtifactHandler.Plugin;

public sealed class BuildContentInfo
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

    /// <summary>
    /// Gets the consumed packages.
    /// </summary>
    public ImmutableArray<NuGetPackageInstance> Consumed => _consumed;

    /// <summary>
    /// Gets the produced packages identifiers.
    /// </summary>
    public ImmutableArray<string> Produced => _produced;

    /// <summary>
    /// Gets the generated asset file names if any.
    /// </summary>
    public ImmutableArray<string> AssetFileNames => _assetFileNames;

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
            var b = ImmutableArray.CreateBuilder<NuGetPackageInstance>( count );
            while( NuGetPackageInstance.TryMatch( ref s, out var p ) )
            {
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
            var b = ImmutableArray.CreateBuilder<string>( count );
            while( TryReadString( ref s, out var text ) )
            {
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
}
