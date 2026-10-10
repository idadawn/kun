#pragma warning disable RS0026 // First argument types differ (string vs typed request); legacy signature is retained.
namespace Kun.Identity.Abstractions;

/// <summary>
/// OpenID Connect client for discovery, PKCE code exchange, token refresh,
/// client credentials and signed back-channel logout validation.
/// </summary>
public interface IOidcClient
{
    /// <summary>
    /// Name under which this client was registered. Useful when a
    /// consumer registers multiple OIDC providers (e.g. both
    /// <c>"authentik"</c> and <c>"google"</c>) and keyed-service
    /// resolution picks one.
    /// </summary>
    string Name { get; }

    /// <summary>Fetches and caches the provider's OpenID configuration / JWKS metadata.</summary>
    Task<OidcConfiguration> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges an authorization <paramref name="code"/> (and
    /// matching <paramref name="redirectUri"/>) for an access /
    /// refresh / id-token triple at the provider's token endpoint.
    /// </summary>
    [Obsolete("Pass an OidcCodeExchange with PKCE and nonce.", DiagnosticId = "KUNOIDC001")]
    Task<OidcTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>Exchanges a code with its original PKCE verifier and expected nonce; validates the returned ID Token.</summary>
    Task<OidcTokenResponse> ExchangeCodeAsync(OidcCodeExchange request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This OIDC client does not implement PKCE exchange.");

    /// <summary>Exchanges a refresh token; a returned ID Token must belong to the expected subject.</summary>
    Task<OidcTokenResponse> RefreshAsync(string refreshToken, string expectedSubject, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This OIDC client does not implement refresh.");

    /// <summary>Obtains a machine access token from the configured identity provider.</summary>
    Task<OidcTokenResponse> ClientCredentialsAsync(string scope, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This OIDC client does not implement client credentials.");

    /// <summary>Validates a signed logout token for this issuer/client; does not itself revoke sessions.</summary>
    Task<OidcLogoutToken> ValidateLogoutTokenAsync(string token, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This OIDC client does not implement logout validation.");
}
