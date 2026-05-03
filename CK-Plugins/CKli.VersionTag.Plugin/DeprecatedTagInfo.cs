using CK.Core;
using CKli.ArtifactHandler.Plugin;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Captures the "+deprecated" annotated tag message.
/// </summary>
[DebuggerDisplay("{ToString()}")]
public sealed class DeprecatedTagInfo
{
    public const string UnspecifiedReason = "(unspecified)";

    readonly BuildContentInfo _contentInfo;
    readonly string _reason;
    readonly DateOnly _expiration;
    readonly int _daysDelay;

    /// <summary>
    /// Initializes a new content.
    /// </summary>
    /// <param name="contentInfo">The build content info from the original version tag.</param>
    /// <param name="expiration">The expiration day.</param>
    /// <param name="days">The delay used to compute the <paramref name="expiration"/> in days.</param>
    /// <param name="reason">The reason.</param>
    public DeprecatedTagInfo( BuildContentInfo contentInfo, DateOnly expiration, int days, string reason = UnspecifiedReason )
    {
        Throw.CheckNotNullArgument( contentInfo );
        Throw.CheckNotNullArgument( reason );
        Throw.CheckOutOfRangeArgument( days >= 0 );
        _contentInfo = contentInfo;
        _reason = reason;
        _expiration = expiration;
        _daysDelay = days;
    }

    /// <summary>
    /// Gets the build content info from the original version tag.
    /// </summary>
    public BuildContentInfo ContentInfo => _contentInfo;

    /// <summary>
    /// Gets the expiration day.
    /// </summary>
    public DateOnly Expiration => _expiration;

    /// <summary>
    /// Gets the optional deprecated reason.
    /// </summary>
    public string Reason => _reason;

    /// <summary>
    /// Gets the delay used to compute the <see cref="Expiration"/> in days.
    /// </summary>
    public int DaysDelay => _daysDelay;

    /// <summary>
    /// Gets whether <see cref="Expiration"/> is before or equal to today (Universal Time Coordinate).
    /// </summary>
    public bool HasExpired => _expiration <= DateOnly.FromDateTime( DateTime.UtcNow );

    /// <summary>
    /// Writes the <see cref="Consumed"/>, <see cref="Produced"/> and <see cref="AssetFileNames"/> as
    /// a text that can be parsed back by <see cref="TryParse(ReadOnlySpan{char}, out BuildContentInfo?)"/>.
    /// </summary>
    /// <param name="b">The target builder.</param>
    /// <returns>The builder.</returns>
    public StringBuilder Write( StringBuilder b )
    {
#if DEBUG
        int previousLength = b.Length;
#endif
        b.Append( "Reason: " ).Append( _reason ).AppendLine();
        b.Append( "Expiration: " ).Append( _expiration.ToString( "o" ) ).Append( " (" ).Append( _daysDelay ).Append(" days)").AppendLine();
        _contentInfo.Write( b );
#if DEBUG
        Throw.Assert( TryParse( b.ToString().AsSpan( previousLength ), out var clone )
                      && clone.DaysDelay == _daysDelay
                      && clone.Expiration == _expiration
                      && clone.Reason == _reason
                      && clone.ContentInfo == _contentInfo );
#endif
        return b;
    }

    /// <summary>
    /// Tries to parse the text content info.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="info">The resulting info on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( ReadOnlySpan<char> text, [NotNullWhen( true )] out DeprecatedTagInfo? info )
    {
        if( text.TryMatch( "Reason:" )
            && text.SkipWhiteSpaces()
            && TryReadString( ref text, out var reason )
            && text.SkipWhiteSpaces()
            && text.TryMatch( "Expiration:" )
            && text.SkipWhiteSpaces()
            && TryReadDateOnly( ref text, out var expiration )
            && text.SkipWhiteSpaces()
            && text.TryMatch( '(' )
            && text.SkipWhiteSpaces()
            && text.TryMatchInteger<int>( out var days )
            && text.SkipWhiteSpaces()
            && text.TryMatch( "days" )
            && text.SkipWhiteSpaces()
            && text.TryMatch( ')' )
            && text.SkipWhiteSpaces()
            && BuildContentInfo.TryParse( text, out var contentInfo ) )
        {
            info = new DeprecatedTagInfo( contentInfo, expiration, days, reason );
            return true;
        }
        info = null;
        return false;

        static bool TryReadString( ref ReadOnlySpan<char> s, [NotNullWhen( true )] out string? text )
        {
            int i = s.IndexOfAny( "\r\n" );
            if( i < 0 )
            {
                text = null;
                return false;
            }
            text = new string( s.Slice( 0, i ) );
            s = s.Slice( i );
            return true;
        }

        static bool TryReadDateOnly( ref ReadOnlySpan<char> s, out DateOnly d )
        {
            // Always "yyyy-MM-dd".
            // https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#the-round-trip-o-o-format-specifier
            if( DateOnly.TryParseExact( s.Slice( 0, 10 ), "o", out d ) )
            {
                s = s.Slice( 10 );
                return true;
            }
            return false;
        }

    }

    /// <summary>
    /// Gets the text content info.
    /// </summary>
    /// <returns>The content info.</returns>
    public override string ToString() => Write( new StringBuilder() ).ToString();

}

