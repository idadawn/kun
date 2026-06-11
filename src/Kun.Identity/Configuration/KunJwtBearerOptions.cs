using Microsoft.IdentityModel.Tokens;

namespace Kun.Identity.Configuration;

/// <summary>
/// Validation parameters for external Bearer JWTs issued by an OIDC
/// provider (Authentik, Keycloak, Auth0, Azure AD, …). Feeds
/// <c>KunJwtValidator</c>.
/// </summary>
public sealed class KunJwtBearerOptions
{
    /// <summary>Expected <c>iss</c> claim. Required — must be set explicitly.</summary>
    public string? Issuer { get; set; }

    /// <summary>Expected <c>aud</c> claim. Required — must be set explicitly.</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// OIDC JWKS URL. When set, signing keys are fetched and rotated
    /// automatically. Leave <see langword="null"/> and populate
    /// <see cref="IssuerSigningKeys"/> for offline / test scenarios.
    /// </summary>
    public string? JwksUri { get; set; }

    /// <summary>Static signing keys — overrides <see cref="JwksUri"/> when set. Primarily for tests.</summary>
    public IList<SecurityKey> IssuerSigningKeys { get; } = [];

    /// <summary>Reject tokens without an <c>exp</c> claim (default <see langword="true"/>).</summary>
    public bool RequireExpirationTime { get; set; } = true;

    /// <summary>Allowed wall-clock skew between issuer and verifier (default 60 seconds).</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Validate the token's signature (default <see langword="true"/>).
    /// Only disable for tests — accepting unsigned tokens in production is a critical security risk.
    /// </summary>
    [Obsolete("Only disable for tests; will be removed in a future version.")]
    public bool ValidateSignature { get; set; } = true;
}
