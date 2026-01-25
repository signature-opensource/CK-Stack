using CK.Core;
using CKli.Core;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Captures credentials that can be used directly (<see cref="NuGetFeed.FakeReadCredentials"/>)
/// or indirectly by resolving the name and secret through <see cref="ISecretsStore"/>.
/// </summary>
public sealed class NuGetFeedCredentials
{
    readonly string? _userNameKey;
    readonly string _secretKey;

    /// <summary>
    /// Initializes a new credentials.
    /// </summary>
    /// <param name="secretKey">The secret key name.</param>
    /// <param name="userNameKey">Optional user name key.</param>
    public NuGetFeedCredentials( string secretKey, string? userNameKey )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( secretKey );
        _userNameKey = userNameKey;
        _secretKey = secretKey;
    }


    /// <summary>
    /// Optional user name to lookup in the <see cref="ISecretsStore"/> (unless used directly).
    /// Null when the secret to use is an API key.
    /// </summary>
    public string? UserNameKey => _userNameKey;

    /// <summary>
    /// Gets the name of the password or the API key to lookup
    /// in the <see cref="ISecretsStore"/> (unless used directly).
    /// </summary>
    public string SecretKey => _secretKey;

    /// <summary>
    /// Gets whether this credentials is an API key (<see cref="UserNameKey"/> is null). 
    /// </summary>
    [MemberNotNullWhen( false, nameof( UserNameKey ) )]
    public bool IsAPIKey => _userNameKey == null;

    internal static NuGetFeedCredentials? Create( XElement? e )
    {
        if( e == null ) return null;
        var n = (string?)e.Attribute( "UserNameKey" ) ?? (string?)e.Element( "UserNameKey" );
        var s = (string?)e.Attribute( "SecretKey" ) ?? (string?)e.Element( "SecretKey" );
        return new NuGetFeedCredentials( s!, n );
    }

}
