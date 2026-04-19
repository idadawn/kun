using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.IntegrationTests.Gateway;

/// <summary>
/// Contract tests for JWKS key rotation: multiple kids coexist in a JWKS, tokens signed
/// by any current key must validate, and tokens signed by an unknown kid must be rejected.
/// </summary>
public sealed class JwksRotationTests : IDisposable
{
    private const string Issuer = "kun-gateway";
    private const string Audience = "kun-internal";

    // Two RSA key pairs representing "old" and "new" keys
    private readonly RSA _rsaOld;
    private readonly RSA _rsaNew;
    private readonly RsaSecurityKey _keyOld;
    private readonly RsaSecurityKey _keyNew;

    // The combined JWKS exposed by the gateway
    private readonly JsonWebKeySet _jwks;

    public JwksRotationTests()
    {
        _rsaOld = RSA.Create(2048);
        _rsaNew = RSA.Create(2048);

        _keyOld = new RsaSecurityKey(_rsaOld) { KeyId = "kid-old" };
        _keyNew = new RsaSecurityKey(_rsaNew) { KeyId = "kid-new" };

        // Build combined JWKS (public keys only)
        var jwkOld = JsonWebKeyConverter.ConvertFromRSASecurityKey(_keyOld);
        var jwkNew = JsonWebKeyConverter.ConvertFromRSASecurityKey(_keyNew);

        _jwks = new JsonWebKeySet();
        _jwks.Keys.Add(jwkOld);
        _jwks.Keys.Add(jwkNew);
    }

    public void Dispose()
    {
        _rsaOld.Dispose();
        _rsaNew.Dispose();
    }

    private TokenValidationParameters BuildValidationParams()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = ["RS256", "ES256"],
            IssuerSigningKeys = _jwks.GetSigningKeys(),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    private static string MintToken(RsaSecurityKey signingKey, string issuer = Issuer, string audience = Audience)
    {
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        var now = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(60),
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, "user-round-trip"),
                new Claim("tenant_id", "tenant-test"),
            ]),
            SigningCredentials = creds,
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.CreateEncodedJwt(descriptor);
    }

    [Fact]
    public void TokenSignedWithOldKey_ValidatesAgainstMultiKeyJwks()
    {
        // Arrange — token minted with kid-old; JWKS contains both kid-old and kid-new
        var token = MintToken(_keyOld);
        var tvp = BuildValidationParams();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var principal = handler.ValidateToken(token, tvp, out _);

        // Assert — should validate because kid-old is still in JWKS
        principal.Should().NotBeNull();
        var jwt = handler.ReadJwtToken(token);
        jwt.Header["kid"].Should().Be("kid-old");
    }

    [Fact]
    public void TokenSignedWithNewKey_ValidatesAgainstMultiKeyJwks()
    {
        // Arrange — token minted with kid-new; JWKS contains both kid-old and kid-new
        var token = MintToken(_keyNew);
        var tvp = BuildValidationParams();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var principal = handler.ValidateToken(token, tvp, out _);

        // Assert — should validate because kid-new is present in JWKS
        principal.Should().NotBeNull();
        var jwt = handler.ReadJwtToken(token);
        jwt.Header["kid"].Should().Be("kid-new");
    }

    [Fact]
    public void TokenSignedWithUnknownKey_IsRejected()
    {
        // Arrange — token minted with a key that is NOT in the JWKS
        using var unknownRsa = RSA.Create(2048);
        var unknownKey = new RsaSecurityKey(unknownRsa) { KeyId = "kid-unknown" };
        var token = MintToken(unknownKey);
        var tvp = BuildValidationParams();
        var handler = new JwtSecurityTokenHandler();

        // Act + Assert — kid-unknown is not in JWKS so signature verification must fail
        var act = () => handler.ValidateToken(token, tvp, out _);
        act.Should().Throw<SecurityTokenSignatureKeyNotFoundException>();
    }
}
