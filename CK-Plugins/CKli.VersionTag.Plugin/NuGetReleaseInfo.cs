using CK.Core;
using CKli.ArtifactHandler.Plugin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace CKli.VersionTag.Plugin;

public sealed class NuGetReleaseInfo
{
    readonly ImmutableArray<NuGetPackageInstance> _consumed;
    readonly ImmutableArray<NuGetPackageInstance> _produced;

    NuGetReleaseInfo( ImmutableArray<NuGetPackageInstance> consumed, ImmutableArray<NuGetPackageInstance> produced )
    {
        _consumed = consumed;
        _produced = produced;
    }

    /// <summary>
    /// Gets the consumed packages.
    /// </summary>
    public ImmutableArray<NuGetPackageInstance> Consumed => _consumed;

    /// <summary>
    /// Gets the produced packages.
    /// </summary>
    public ImmutableArray<NuGetPackageInstance> Produced => _produced;

    public static bool TryParseMessage( ReadOnlySpan<char> message, [NotNullWhen( true )] out NuGetReleaseInfo? info )
    {
        if( message.TryMatchInteger<int>( out var consumedCount )
            && message.SkipWhiteSpaces()
            && message.TryMatch( "Consumed Packages:" )
            && TryReadList( ref message, consumedCount, out var consumed )
            && message.SkipWhiteSpaces() 
            && message.TryMatchInteger<int>( out var producedCount )
            && message.SkipWhiteSpaces()
            && message.TryMatch( "Produced Packages:" )
            && TryReadList( ref message, producedCount, out var produced ) )
        {
            info = new NuGetReleaseInfo( consumed, produced );
            return true;
        }
        info = null;
        return false;

        static bool TryReadList( ref ReadOnlySpan<char> s, int count, out ImmutableArray<NuGetPackageInstance> packages )
        {
            var b = ImmutableArray.CreateBuilder<NuGetPackageInstance>( count );
            while( NuGetPackageInstance.TryMatch( ref s, out var p ) )
            {
                b.Add( p );
                s.SkipWhiteSpaces();
                if( !s.TryMatch(',')) break;
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

    }

    /// <summary>
    /// Appends the consumed/produced message to the string builder.
    /// Can be parsed back by <see cref="TryParseMessage(ReadOnlySpan{char}, out NuGetReleaseInfo?)"/>.
    /// </summary>
    /// <param name="b">The builder.</param>
    /// <param name="consumed">The consumed packages.</param>
    /// <param name="produced">The produced packages.</param>
    /// <returns>The builder.</returns>
    public static StringBuilder AppendMessage( StringBuilder b,
                                               IReadOnlySet<NuGetPackageInstance> consumed,
                                               List<LocalNuGetPackageInstance> produced )
    {
        return b.Append( consumed.Count ).Append( " Consumed Packages:" ).AppendLine()
                .AppendJoin( ", ", consumed )
                .Append( produced.Count ).Append( " Produced Packages:" ).AppendLine()
                .AppendJoin( ", ", produced ).AppendLine();
    }
}
