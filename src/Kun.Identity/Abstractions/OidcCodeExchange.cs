namespace Kun.Identity.Abstractions;

/// <summary>Authorization code and the original browser transaction's PKCE verifier and nonce.</summary>
public sealed record OidcCodeExchange(string Code, string RedirectUri, string CodeVerifier, string ExpectedNonce);

/// <summary>Validated back-channel logout instruction. Subject or SessionId (or both) identifies sessions within Issuer.</summary>
public sealed record OidcLogoutToken(string Issuer, string? Subject, string? SessionId, string TokenId, DateTimeOffset ExpiresAt);
