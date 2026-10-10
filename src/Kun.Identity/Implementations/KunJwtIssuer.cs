using System.Security.Claims;
using System.Security.Cryptography;
using Kun.Identity.Abstractions;
using Kun.Identity.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Identity.Implementations;

/// <summary>
/// <see cref="IJwtIssuer"/> backed by
/// <see cref="JsonWebTokenHandler"/>. Signs tokens with the configured
/// RSA key — supplied either as a PEM string or as a
/// pre-built <see cref="SecurityKey"/> (the test-friendly path).
/// </summary>
[Obsolete("Use Kun.Gateway.S2sTokenIssuer for canonical short-lived S2S tokens.", DiagnosticId = "KUNS2S001")]
public sealed class KunJwtIssuer : IJwtIssuer
{
    private readonly KunJwtIssuerOptions _options;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials;

    /// <summary>Creates an issuer bound to the supplied options.</summary>
    public KunJwtIssuer(IOptions<KunJwtIssuerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        var key = _options.SigningKey ?? BuildKeyFromPem(_options.SigningKeyPem);
        if (!string.IsNullOrWhiteSpace(_options.KeyId))
        {
            key.KeyId = _options.KeyId;
        }
        _credentials = new SigningCredentials(key, _options.Algorithm);
    }

    /// <inheritdoc />
    public string Issue(IEnumerable<Claim> claims, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime ?? _options.DefaultLifetime),
            SigningCredentials = _credentials,
        };
        return _handler.CreateToken(descriptor);
    }

    private static RsaSecurityKey BuildKeyFromPem(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                "KunJwtIssuerOptions requires either SigningKey or SigningKeyPem to be set.");
        }
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa);
    }
}
