namespace Kun.Identity.Abstractions;

/// <summary>
/// Thin OpenID Connect client covering the pieces Kun consumers
/// actually need: provider discovery and the authorization-code
/// token exchange. Refresh-token, client-credentials, and
/// device-flow grants are out of scope for the initial release.
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
    Task<OidcTokenResponse> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);
}
