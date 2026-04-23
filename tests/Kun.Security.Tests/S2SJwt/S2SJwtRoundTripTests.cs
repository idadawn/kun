using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Kun.Security.S2SJwt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Kun.Security.Tests.S2SJwt;

public sealed class S2SJwtRoundTripTests : IDisposable
{
    private readonly ECDsa _ecdsa;
    private readonly string _privateKeyPem;
    private readonly S2SJwtOptions _options;
    private readonly CancellationToken _ct;

    public S2SJwtRoundTripTests()
    {
        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _privateKeyPem = _ecdsa.ExportECPrivateKeyPem();

        _options = new S2SJwtOptions
        {
            SigningKeyPem = _privateKeyPem,
            KeyId = "test-key-1",
            Issuer = "neuroagent-edge",
            DefaultLifetimeSeconds = 300,
        };

        _ct = TestContext.Current.CancellationToken;
    }

    [Fact]
    public async Task RoundTrip_IssueAndVerify_Succeeds()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            "user-001", "tenant-001", ["threads:read", "threads:write"],
            ["neuroagent-platform"], "session-001", _ct);

        var principal = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);

        principal.Should().NotBeNull();
        principal!.FindFirst("sub")!.Value.Should().Be("user-001");
        principal.FindFirst("tenant_id")!.Value.Should().Be("tenant-001");
        principal.FindFirst("scope")!.Value.Should().Be("threads:read threads:write");
        principal.FindFirst("session_id")!.Value.Should().Be("session-001");
        // Issuer is validated during VerifyAsync; ClaimsPrincipal doesn't expose "iss" as a direct claim
    }

    [Fact]
    public async Task ExpiredToken_Verify_ReturnsNull()
    {
        // Create an expired token directly (past timestamps, outside 30s clock skew)
        var ecdsaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsaKey.ImportFromPem(_privateKeyPem);
        var key = new ECDsaSecurityKey(ecdsaKey) { KeyId = "test-key-1" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "neuroagent-edge",
            Audience = "neuroagent-platform",
            NotBefore = now.AddMinutes(-5),
            IssuedAt = now.AddMinutes(-5),
            Expires = now.AddMinutes(-2),
            Subject = new ClaimsIdentity([new Claim("sub", "user-001")]),
            SigningCredentials = creds,
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateEncodedJwt(descriptor);

        var verifier = CreateVerifier();
        var result = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task WrongAudience_Verify_ReturnsNull()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            "user-001", "tenant-001", ["threads:read"],
            ["neuroagent-platform"], "session-001", _ct);

        // Verify with wrong audience
        var result = await verifier.VerifyAsync(token, "neuroagent-storage", _ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task KeyRotation_PreviousKeyStillValid()
    {
        // Create a second key for rotation
        var ecdsa2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var prevKeyPem = ecdsa2.ExportECPrivateKeyPem();

        // Issue token with previous key
        var prevOptions = new S2SJwtOptions
        {
            SigningKeyPem = prevKeyPem,
            KeyId = "prev-key",
            Issuer = "neuroagent-edge",
            DefaultLifetimeSeconds = 300,
        };

        var issuer = CreateIssuer(prevOptions);

        // Verifier knows both keys
        var rotationOptions = new S2SJwtOptions
        {
            SigningKeyPem = _privateKeyPem,
            KeyId = "test-key-1",
            PreviousSigningKeyPem = prevKeyPem,
            PreviousKeyId = "prev-key",
            Issuer = "neuroagent-edge",
            DefaultLifetimeSeconds = 300,
        };
        var verifier = CreateVerifier(rotationOptions);

        var token = await issuer.IssueAsync(
            "user-001", "tenant-001", ["threads:read"],
            ["neuroagent-platform"], "session-001", _ct);

        var result = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);

        result.Should().NotBeNull();
        result!.FindFirst("sub")!.Value.Should().Be("user-001");

        ecdsa2.Dispose();
    }

    private S2SJwtIssuer CreateIssuer(S2SJwtOptions? options = null)
    {
        var opts = Options.Create(options ?? _options);
        var logger = Substitute.For<ILogger<S2SJwtIssuer>>();
        return new S2SJwtIssuer(opts, logger);
    }

    private S2SJwtVerifier CreateVerifier(S2SJwtOptions? options = null)
    {
        var opts = Options.Create(options ?? _options);
        var logger = Substitute.For<ILogger<S2SJwtVerifier>>();
        return new S2SJwtVerifier(opts, logger);
    }

    public void Dispose() => _ecdsa.Dispose();
}
