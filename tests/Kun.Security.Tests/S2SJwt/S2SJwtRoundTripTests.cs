#pragma warning disable KUNS2S001 // Explicit compatibility tests for retired issuer APIs; new consumers must fail compilation.
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
            Issuer = "kun-gateway",
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
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                Scopes: ["threads:read", "threads:write"],
                SessionId: "session-001"),
            _ct);

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
            Issuer = "kun-gateway",
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
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                Scopes: ["threads:read"],
                SessionId: "session-001"),
            _ct);

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
            Issuer = "kun-gateway",
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
            Issuer = "kun-gateway",
            DefaultLifetimeSeconds = 300,
        };
        var verifier = CreateVerifier(rotationOptions);

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                Scopes: ["threads:read"],
                SessionId: "session-001"),
            _ct);

        var result = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);

        result.Should().NotBeNull();
        result!.FindFirst("sub")!.Value.Should().Be("user-001");

        ecdsa2.Dispose();
    }

    [Fact]
    public async Task Roles_AreEmittedAsMultiple_Claims()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-roles",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                Roles: ["admin", "editor", "viewer"]),
            _ct);

        var principal = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);
        principal.Should().NotBeNull();
        principal!.FindAll("roles").Select(c => c.Value)
            .Should().BeEquivalentTo(["admin", "editor", "viewer"]);
    }

    [Fact]
    public async Task MembershipId_IsEmitted_WhenProvided()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                MembershipId: "mbr-42"),
            _ct);

        var principal = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);
        principal!.FindFirst("membership_id")!.Value.Should().Be("mbr-42");
    }

    [Fact]
    public async Task MembershipId_IsAbsent_When_NotProvided()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"]),
            _ct);

        var principal = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);
        principal!.FindFirst("membership_id").Should().BeNull();
    }

    [Fact]
    public async Task ProjectId_IsEmitted_WhenProvided()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                ProjectId: "22222222-2222-2222-2222-222222222222"),
            _ct);

        var principal = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);
        principal!.FindFirst("project_id")!.Value
            .Should().Be("22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public async Task ProjectId_IsAbsent_When_NotProvided()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"]),
            _ct);

        var principal = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);
        principal!.FindFirst("project_id").Should().BeNull();
    }

    [Fact]
    public async Task TraceId_IsEmitted_WhenProvided()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                TraceId: "0af7651916cd43dd8448eb211c80319c"),
            _ct);

        var principal = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);
        principal!.FindFirst("trace_id")!.Value.Should().Be("0af7651916cd43dd8448eb211c80319c");
    }

    [Fact]
    public async Task TraceId_IsAbsent_When_NotProvided()
    {
        var issuer = CreateIssuer();
        var verifier = CreateVerifier();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"]),
            _ct);

        var principal = await verifier.VerifyAsync(token, "neuroagent-platform", _ct);
        principal!.FindFirst("trace_id").Should().BeNull();
    }

    [Fact]
    public async Task DefaultLifetime_IsFiveMinutes_WhenOptionsLeaveItUnset()
    {
        var defaultOptions = new S2SJwtOptions
        {
            SigningKeyPem = _privateKeyPem,
            KeyId = "test-key-1",
            Issuer = "kun-gateway",
            // DefaultLifetimeSeconds left at the type default (300s = 5 minutes,
            // upper bound of whitepaper §9.6 zero-trust TTL range).
        };
        var issuer = CreateIssuer(defaultOptions);

        var before = DateTime.UtcNow;
        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"]),
            _ct);
        var after = DateTime.UtcNow;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var expectedMin = before.AddMinutes(5).AddSeconds(-5);
        var expectedMax = after.AddMinutes(5).AddSeconds(5);
        jwt.ValidTo.Should().BeOnOrAfter(expectedMin).And.BeOnOrBefore(expectedMax);
    }

    [Fact]
    public async Task Lifetime_Override_Applies_When_Provided_In_Request()
    {
        var issuer = CreateIssuer();

        var before = DateTime.UtcNow;
        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform"],
                Lifetime: TimeSpan.FromSeconds(30)),
            _ct);
        var after = DateTime.UtcNow;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.ValidTo.Should().BeOnOrAfter(before.AddSeconds(25))
            .And.BeOnOrBefore(after.AddSeconds(35));
    }

    [Fact]
    public async Task MultiAudience_IsEmittedInPayload_NotHeader()
    {
        var issuer = CreateIssuer();

        var token = await issuer.IssueAsync(
            new S2SJwtIssueRequest(
                UserId: "user-001",
                TenantId: "tenant-001",
                Audience: ["neuroagent-platform", "neuroagent-storage"]),
            _ct);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Audiences.Should().BeEquivalentTo(["neuroagent-platform", "neuroagent-storage"]);
        // aud must live in payload, not header
        jwt.Header.Should().NotContainKey("aud");
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
