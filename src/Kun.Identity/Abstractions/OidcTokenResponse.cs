using System.Security.Claims;

namespace Kun.Identity.Abstractions;

/// <summary>Result of an OIDC authorization-code exchange.</summary>
/// <param name="AccessToken">The bearer access token to present to resource servers.</param>
/// <param name="IdToken">The OIDC id-token; <see langword="null"/> when the provider does not issue one for this grant.</param>
/// <param name="RefreshToken">Refresh token to obtain new access tokens; <see langword="null"/> when not offered.</param>
/// <param name="TokenType">Token type hint returned by the server (typically <c>Bearer</c>).</param>
/// <param name="ExpiresIn">Access-token lifetime in seconds as reported by the server.</param>
public sealed record OidcTokenResponse(
    string AccessToken,
    string? IdToken,
    string? RefreshToken,
    string TokenType,
    int ExpiresIn)
{
    /// <summary>Identity validated by the OIDC client for this grant; null for machine grants without an ID Token.</summary>
    public ClaimsPrincipal? ValidatedPrincipal { get; internal init; }
}
