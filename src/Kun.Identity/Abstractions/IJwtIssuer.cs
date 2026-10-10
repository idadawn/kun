using System.Security.Claims;

namespace Kun.Identity.Abstractions;

/// <summary>
/// Mints signed JWTs for internal service-to-service calls. The
/// issuer, audience, signing key, and default lifetime come from the
/// registered <c>KunJwtIssuerOptions</c>; callers pass the claims
/// describing <i>this</i> token (subject, tenant, roles, …).
/// </summary>
[Obsolete("Use Kun.Gateway.S2sTokenIssuer for canonical short-lived S2S tokens.", DiagnosticId = "KUNS2S001")]
public interface IJwtIssuer
{
    /// <summary>
    /// Creates a signed JWT carrying <paramref name="claims"/>. When
    /// <paramref name="lifetime"/> is <see langword="null"/>, the
    /// configured <c>DefaultLifetime</c> applies. Returns the compact
    /// serialization ready to place in an <c>Authorization: Bearer</c>
    /// header.
    /// </summary>
    string Issue(IEnumerable<Claim> claims, TimeSpan? lifetime = null);
}
