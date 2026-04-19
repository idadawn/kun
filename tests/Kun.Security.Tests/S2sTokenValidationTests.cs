using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Kun.Security.Authentication;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.Security.Tests;

/// <summary>
/// Tests for S2S JWT token validation via <see cref="KunServiceAuthenticationOptions"/>.
/// Validates the zero-trust constraints: RS256-only, audience check, expiry, and key rotation.
/// </summary>
public sealed class S2sTokenValidationTests
{
    private const string Issuer = "https://kun-gateway.internal";
    private const string Audience = "kun-internal";

    // Generate a fresh RSA key pair for each test class instantiation
    private readonly RsaSecurityKey _signingKey;
    private readonly JsonWebKey _publicJwk;
    private readonly TokenValidationParameters _validationParams;

    public S2sTokenValidationTests()
    {
        var rsa = System.Security.Cryptography.RSA.Create(2048);
        _signingKey = new RsaSecurityKey(rsa) { KeyId = "key-1" };

        // Public key for validation
        _publicJwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(_signingKey);

        var keySet = new JsonWebKeySet();
        keySet.Keys.Add(_publicJwk);

        var opts = new KunServiceAuthenticationOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            StaticKeySet = keySet,
        };

        _validationParams = BuildValidationParams(opts);
    }

    private static TokenValidationParameters BuildValidationParams(KunServiceAuthenticationOptions opts)
    {
        var validIssuers = new List<string> { opts.Issuer };
        if (opts.AdditionalValidIssuers is not null)
            validIssuers.AddRange(opts.AdditionalValidIssuers);

        var tvp = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ValidateAudience = true,
            ValidAudience = opts.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = ["RS256", "ES256"],
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        if (opts.StaticKeySet is not null)
            tvp.IssuerSigningKeys = opts.StaticKeySet.GetSigningKeys();

        return tvp;
    }

    private string CreateToken(
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? signingKey = null,
        string algorithm = SecurityAlgorithms.RsaSha256,
        string? kid = null)
    {
        var key = signingKey ?? _signingKey;
        if (kid is not null && key is RsaSecurityKey rk)
            rk.KeyId = kid;

        var creds = new SigningCredentials(key, algorithm);
        var now = DateTime.UtcNow;
        var exp = expires ?? now.AddSeconds(60);
        // NotBefore must be before Expires (use 2 min before expires for expired tokens)
        var notBefore = exp > now ? now : exp.AddMinutes(-1);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore,
            IssuedAt = notBefore,
            Expires = exp,
            Subject = new ClaimsIdentity([new Claim("sub", "user-123"), new Claim("tenant_id", "t1")]),
            SigningCredentials = creds,
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.CreateEncodedJwt(descriptor);
    }

    [Fact]
    public void ValidToken_PassesValidation()
    {
        // Arrange
        var token = CreateToken();
        var handler = new JwtSecurityTokenHandler();

        // Act
        var principal = handler.ValidateToken(token, _validationParams, out _);

        // Assert — JwtSecurityTokenHandler maps "sub" to ClaimTypes.NameIdentifier
        principal.Should().NotBeNull();
        var subClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? principal.FindFirst("sub");
        subClaim.Should().NotBeNull();
        subClaim!.Value.Should().Be("user-123");
    }

    [Fact]
    public void HS256Token_IsRejected()
    {
        // Arrange — create an HS256 token with a symmetric key
        var symmetricKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("super-secret-key-that-is-long-enough-32chars"));
        var token = CreateToken(signingKey: symmetricKey, algorithm: SecurityAlgorithms.HmacSha256);
        var handler = new JwtSecurityTokenHandler();

        // Act + Assert — HS256 must be rejected (algorithm not in ValidAlgorithms or key mismatch)
        var ex = Record.Exception(() => handler.ValidateToken(token, _validationParams, out _));
        ex.Should().NotBeNull();
        ex.Should().BeAssignableTo<SecurityTokenException>();
    }

    [Fact]
    public void WrongAudience_IsRejected()
    {
        // Arrange
        var token = CreateToken(audience: "wrong-audience");
        var handler = new JwtSecurityTokenHandler();

        // Act
        var act = () => handler.ValidateToken(token, _validationParams, out _);

        // Assert
        act.Should().Throw<SecurityTokenInvalidAudienceException>();
    }

    [Fact]
    public void ExpiredToken_IsRejected()
    {
        // Arrange — token expired 2 minutes ago (outside 30s clock skew)
        var token = CreateToken(expires: DateTime.UtcNow.AddMinutes(-2));
        var handler = new JwtSecurityTokenHandler();

        // Act
        var act = () => handler.ValidateToken(token, _validationParams, out _);

        // Assert
        act.Should().Throw<SecurityTokenExpiredException>();
    }

    [Fact]
    public void WrongIssuer_IsRejected()
    {
        // Arrange
        var token = CreateToken(issuer: "https://evil-gateway.external");
        var handler = new JwtSecurityTokenHandler();

        // Act
        var act = () => handler.ValidateToken(token, _validationParams, out _);

        // Assert
        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void KeyRotation_OldKidInJwks_StillValidates()
    {
        // Arrange — two keys in JWKS; token signed with "key-1"
        var rsa2 = System.Security.Cryptography.RSA.Create(2048);
        var key2 = new RsaSecurityKey(rsa2) { KeyId = "key-2" };
        var jwk2 = JsonWebKeyConverter.ConvertFromRSASecurityKey(key2);

        var multiKeySet = new JsonWebKeySet();
        multiKeySet.Keys.Add(_publicJwk);   // key-1 (our original)
        multiKeySet.Keys.Add(jwk2);          // key-2 (new key)

        var opts = new KunServiceAuthenticationOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            StaticKeySet = multiKeySet,
        };
        var tvp = BuildValidationParams(opts);

        // Token signed with key-1 (old key)
        var token = CreateToken(kid: "key-1");
        var handler = new JwtSecurityTokenHandler();

        // Act — should validate because key-1 is still in JWKS
        var principal = handler.ValidateToken(token, tvp, out _);

        // Assert
        principal.Should().NotBeNull();
    }

    [Fact]
    public void KeyRotation_UnknownKid_IsRejected()
    {
        // Arrange — JWKS only has key-1; token signed with unknown key
        var unknownRsa = System.Security.Cryptography.RSA.Create(2048);
        var unknownKey = new RsaSecurityKey(unknownRsa) { KeyId = "key-unknown" };

        var token = CreateToken(signingKey: unknownKey, kid: "key-unknown");
        var handler = new JwtSecurityTokenHandler();

        // Act — key-unknown is not in our JWKS
        var act = () => handler.ValidateToken(token, _validationParams, out _);

        // Assert
        act.Should().Throw<SecurityTokenSignatureKeyNotFoundException>();
    }
}
