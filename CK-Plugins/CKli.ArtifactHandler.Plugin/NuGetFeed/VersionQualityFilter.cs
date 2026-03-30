using CK.Core;
using CSemVer;
using System;
using System.Text;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Defines a filter for version that relies on the prerelease name (or whether the version is stable) and
/// the CI build indicator.
/// <para>
/// <list type="bullet">
///     <item><c>[,],ci</c><term></term>
///     <description>
///     Any version is accepted (Min = null, IncludeMin = true, Max = null, IncludeMax = true, AllowCI = true).
///     This is the <c>default</c> that can be used to configure a "preview" feed (in addition to the
///     official https://www.nuget.org/ that is more restrictive).
///     </description>
///     </item>
///     <item>[pre,]<term></term>
///     <description>
///     Any "pre" version and above including stable ones but not their CI builds (Min = "pre", IncludeMin = true,
///     Max = null, IncludeMax = true, AllowCI = false).
///     This is the regular configuration for the official https://www.nuget.org/ feed (it cannot handle package version
///     with the "--ci" double dash prerelease name and we don"t want to pollute this open feed with really unstable versions).
///     </description>
///     </item>
///     <item><c>[,pre),ci</c><term></term>
///     <description>
///     Any prerelease up to "pre" (but excluding it) with their CI builds (Min = null, Max = "pre", IncludeMax = false, AllowCI = true).
///     This is a possible configuration for a feed that complements the official https://www.nuget.org/ feed
///     (versions are on one or the other feed, not on both).
///     </description>
///     </item>
/// </list>
/// </para>
/// </summary>
public readonly struct VersionQualityFilter
{
    readonly string? _min;
    readonly string? _max;
    readonly bool _excludeMin;
    readonly bool _excludeMax;
    readonly bool _rejectCI;

    /// <summary>
    /// Gets the minimal prerelease name.
    /// </summary>
    public string? Min => _min;

    /// <summary>
    /// Gets the maximal prerelease name.
    /// </summary>
    public string? Max => _max;

    /// <summary>
    /// Gets whether CI versions are allowed.
    /// </summary>
    public bool AllowCI => !_rejectCI;

    /// <summary>
    /// Gets whether stable versions are accepted.
    /// </summary>
    public bool AllowStable => _max == null && !_excludeMax;

    /// <summary>
    /// Gets whether <see cref="Min"/> is included (it is a minimum, not a infimum).
    /// </summary>
    public bool IncludeMin => !_excludeMin;

    /// <summary>
    /// Gets whether <see cref="Max"/> is included (it is a maximum, not a supremum).
    /// </summary>
    public bool IncludeMax => !_excludeMax;

    /// <summary>
    /// Checks whether this range allows the specified prerelease name (regardless of <see cref="AllowCI"/>).
    /// <para>
    /// An empty string corresponds to a stable version.
    /// </para>
    /// </summary>
    /// <param name="prereleaseName">The name to challenge.</param>
    /// <returns>Whether <paramref name="prereleaseName"/> is accepted or not.</returns>
    public bool AcceptsPreleaseName( ReadOnlySpan<char> prereleaseName )
    {
        return prereleaseName.CompareTo( _min.AsSpan(), StringComparison.Ordinal ) >= (_excludeMin ? 1 : 0)
               && _max.AsSpan().CompareTo( prereleaseName, StringComparison.Ordinal ) <= (_excludeMax ? 1 : 0);
    }

    /// <summary>
    /// Checks whether this range allows the specified version.
    /// </summary>
    /// <param name="v">The version to challenge.</param>
    /// <returns></returns>
    public bool Accepts( SVersion v )
    {
        if( !v.IsValid ) return false;
        var r = v.Prerelease.AsSpan();
        if( r.Length == 0 ) return AllowStable;
        if( v.IsCI() && _rejectCI )
        {
            return false;
        }
        return AcceptsPreleaseName( r[0..r.IndexOf( '.' )] );
    }

    /// <summary>
    /// Initializes a new filter.
    /// Min must be lower or equal to max (or max is null) using <see cref="StringComparer.Ordinal"/> otherwise an <see cref="ArgumentException"/> is thrown.
    /// </summary>
    /// <param name="min">See <see cref="Min"/>. When empty or white space, this is normalized to null.</param>
    /// <param name="includeMin">See <see cref="IncludeMin"/>.</param>
    /// <param name="max">See <see cref="Max"/>.</param>
    /// <param name="includeMax">See <see cref="IncludeMax"/>. When empty or white space, this is normalized to null.</param>
    /// <param name="allowCI">See <see cref="AllowCI"/>.</param>
    public VersionQualityFilter( string? min, bool includeMin, string? max, bool includeMax, bool allowCI )
    {
        if( string.IsNullOrWhiteSpace( min ) ) min = null;
        if( string.IsNullOrWhiteSpace( max ) ) max = null;
        Throw.CheckArgument( max == null || StringComparer.Ordinal.Compare( min, max ) <= 0 );
        _min = min;
        _excludeMin = !includeMin;
        _max = max;
        _excludeMax = !includeMax;
        _rejectCI = !allowCI;
    }

    /// <summary>
    /// Initializes a new filter from a string.
    /// Throws an <see cref="ArgumentException"/> on invalid syntax.
    /// Simply uses <see cref="TryParse(ReadOnlySpan{char}, out VersionQualityFilter)"/>) to handle invalid syntax.
    /// </summary>
    /// <param name="s">The string.</param>
    public VersionQualityFilter( ReadOnlySpan<char> s )
    {
        if( !TryParse( s, out VersionQualityFilter p ) ) throw new ArgumentException( "Invalid VersionQualityFilter syntax." );
        _min = p._min;
        _max = p._max;
    }

    /// <summary>
    /// Attempts to parse a string as a <see cref="VersionQualityFilter"/>.
    /// White spaces are silently ignored.
    /// <para>
    /// </summary>
    /// <param name="head">The string to parse (leading and internal white spaces between tokens are skipped).</param>
    /// <param name="filter">The result.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryParse( ReadOnlySpan<char> head, out VersionQualityFilter filter ) => TryMatch( ref head, out filter );

    /// <summary>
    /// Attempts to match a string as a <see cref="VersionQualityFilter"/> (<paramref name="head"/> is forwarded on success).
    /// White spaces are silently ignored.
    /// <para>
    /// </summary>
    /// <param name="head">The string to parse (leading and internal white spaces between tokens are skipped).</param>
    /// <param name="filter">The result.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, out VersionQualityFilter filter )
    {
        filter = default;
        string? min = null;
        string? max = null;

        var h = head.TrimStart();
        bool includeMin = h.TryMatch( '[' );
        if( !includeMin && !h.TryMatch('(') ) return false;

        int comma = h.IndexOf( ',' );
        if( comma < 0 ) return false;
        if( comma > 0 )
        {
            var s = h.Slice( 0, comma ).TrimEnd();
            if( s.Length > 0 ) min = new string( s );
        }
        h = h.Slice( comma + 1 ).TrimStart();
        int end = h.IndexOfAny( ')', ']' );
        if( end < 0 ) return false;
        if( end > 0 )
        {
            var s = h.Slice( 0, end ).TrimEnd();
            if( s.Length > 0 ) max = new string( s );
        }
        bool includeMax = h[end] == ']';
        h = h.Slice( end + 1 );

        bool allowCI = false;
        var lookup = h.TrimStart();
        if( lookup.TryMatch( ',' ) && lookup.SkipWhiteSpaces() && lookup.TryMatch( "ci" ) )
        {
            if( lookup.Length != 0 && !char.IsWhiteSpace( lookup[0] ) )
            {
                return false;
            }
            allowCI = true;
            h = lookup;
        }

        head = h;
        filter = new VersionQualityFilter( min, includeMin, max, includeMax, allowCI );
        return true;
    }

    /// <summary>
    /// Overridden to return "[<see cref="Min"/>,<see cref="Max"/>].ci" string representation.
    /// </summary>
    /// <returns>The filter string representation.</returns>
    public override string ToString()
    {
        StringBuilder b = new StringBuilder();
        b.Append( _excludeMin ? '(' : '[' );
        b.Append( _min ).Append( ',' ).Append( _max );
        b.Append( _excludeMax ? ')' : ']' );
        if( !_rejectCI ) b.Append( ",ci" );
        return b.ToString();
    }
}
