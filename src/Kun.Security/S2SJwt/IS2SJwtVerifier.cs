using System.Security.Claims;

namespace Kun.Security.S2SJwt;

/// <summary>
/// Verifies S2S JWT tokens issued by the gateway.
/// </summary>
[Obsolete("Use Kun.Gateway.S2sTokenIssuer and Kun.Security service authentication.", DiagnosticId = "KUNS2S001")]
public interface IS2SJwtVerifier
{
    /// <summary>
    /// Verifies a token and returns the principal, or <c>null</c> if invalid.
    /// </summary>
    /// <param name="token">The encoded JWT string.</param>
    /// <param name="expectedAudience">The audience the verifier expects.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The validated <see cref="ClaimsPrincipal"/>, or <c>null</c> if validation fails.</returns>
    Task<ClaimsPrincipal?> VerifyAsync(string token, string expectedAudience, CancellationToken ct = default);
}
