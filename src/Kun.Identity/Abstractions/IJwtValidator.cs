using System.Security.Claims;

namespace Kun.Identity.Abstractions;

/// <summary>
/// Validates a Bearer JWT against the configured issuer / audience /
/// signing keys and returns the resulting <see cref="ClaimsPrincipal"/>
/// on success. Returns <see langword="null"/> when validation fails —
/// callers decide whether to surface a 401 or fall through.
/// </summary>
public interface IJwtValidator
{
    /// <summary>
    /// Validates <paramref name="token"/>. Keys are resolved from the
    /// configured JWKS URI with automatic rotation, or from statically
    /// registered <c>IssuerSigningKeys</c> when set. Expiry, issuer,
    /// audience, and lifetime are all checked; tokens failing any
    /// check return <see langword="null"/>.
    /// </summary>
    Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken = default);
}
