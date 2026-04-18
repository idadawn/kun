using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.Authentication;

/// <summary>
/// Options for configuring S2S JWT Bearer authentication on a downstream service.
/// Supports both static key sets and dynamic JWKS endpoint resolution.
/// </summary>
public sealed class KunServiceAuthenticationOptions
{
    /// <summary>Expected token issuer. Required.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Expected audience. Defaults to <c>kun-internal</c>.</summary>
    public string Audience { get; set; } = "kun-internal";

    /// <summary>
    /// Optional URI to a remote JWKS endpoint for fetching signing keys.
    /// Mutually exclusive with <see cref="StaticKeySet"/>.
    /// </summary>
    public string? JwksUri { get; set; }

    /// <summary>
    /// Optional statically-configured key set (e.g. from file or Key Vault).
    /// Mutually exclusive with <see cref="JwksUri"/>.
    /// </summary>
    public JsonWebKeySet? StaticKeySet { get; set; }

    /// <summary>
    /// Additional valid issuers for multi-issuer / blue-green gateway deployments.
    /// <see cref="Issuer"/> is always included.
    /// </summary>
    public IReadOnlyList<string>? AdditionalValidIssuers { get; set; }
}
