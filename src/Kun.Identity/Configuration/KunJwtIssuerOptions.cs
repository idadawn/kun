using Microsoft.IdentityModel.Tokens;

namespace Kun.Identity.Configuration;

/// <summary>Signing / claims configuration for <c>KunJwtIssuer</c>.</summary>
[Obsolete("Use Kun.Gateway.S2sTokenIssuer for canonical short-lived S2S tokens.", DiagnosticId = "KUNS2S001")]
public sealed class KunJwtIssuerOptions
{
    /// <summary>Value written to the <c>iss</c> claim. Required.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Value written to the <c>aud</c> claim. Required.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// RSA private key in PEM format. When set, the issuer builds an
    /// <see cref="RsaSecurityKey"/> from it. Mutually exclusive with
    /// <see cref="SigningKey"/> — caller picks one.
    /// </summary>
    public string? SigningKeyPem { get; set; }

    /// <summary>
    /// Explicit <see cref="SecurityKey"/> override. Primarily for tests
    /// that want to share an in-memory RSA handle with the validator.
    /// </summary>
    public SecurityKey? SigningKey { get; set; }

    /// <summary>JWS algorithm; defaults to <c>RS256</c>.</summary>
    public string Algorithm { get; set; } = SecurityAlgorithms.RsaSha256;

    /// <summary>Default token lifetime when the caller does not pass an explicit value. Defaults to 60 seconds.</summary>
    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Key ID written to the JWT header (<c>kid</c>). Optional — many providers derive it from the key itself.</summary>
    public string? KeyId { get; set; }
}
