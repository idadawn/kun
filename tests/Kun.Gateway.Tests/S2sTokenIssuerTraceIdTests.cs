using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Kun.Gateway.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Kun.Gateway.Tests;

/// <summary>
/// Covers the trace_id claim propagation introduced in P0-2 (commit 4564278).
/// The issuer only emits trace_id when BOTH an active Activity is on the call
/// stack AND an ambient HttpContext exists (M3 guard — stops background token
/// refresh paths from poisoning downstream trace continuity).
/// </summary>
public sealed class S2sTokenIssuerTraceIdTests : IDisposable
{
    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;
    private readonly RSA _rsa;

    public S2sTokenIssuerTraceIdTests()
    {
        _rsa = RSA.Create(2048);
        _privateKeyPath = Path.GetTempFileName();
        _publicKeyPath = Path.GetTempFileName();
        File.WriteAllText(_privateKeyPath, _rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_publicKeyPath, _rsa.ExportRSAPublicKeyPem());
    }

    public void Dispose()
    {
        _rsa.Dispose();
        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);
    }

    [Fact]
    public void IssueFor_ActivityAndHttpContext_EmitsTraceIdClaim()
    {
        var issuer = BuildIssuer(emitTraceId: true, hasHttpContext: true);
        var principal = BuildPrincipal("u1", "t1");

        using var activity = new Activity("test").Start();

        var token = issuer.IssueFor(principal);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var traceIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "trace_id");
        traceIdClaim.Should().NotBeNull();
        traceIdClaim!.Value.Should().Be(activity.TraceId.ToHexString());
    }

    [Fact]
    public void IssueFor_ActivityButNoHttpContext_SuppressesTraceIdClaim()
    {
        // M3 guard: background refresh contexts must NOT emit trace_id.
        var issuer = BuildIssuer(emitTraceId: true, hasHttpContext: false);
        var principal = BuildPrincipal("u1", "t1");

        using var activity = new Activity("background").Start();

        var token = issuer.IssueFor(principal);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "trace_id");
    }

    [Fact]
    public void IssueFor_NoActivity_SuppressesTraceIdClaim()
    {
        var issuer = BuildIssuer(emitTraceId: true, hasHttpContext: true);
        var principal = BuildPrincipal("u1", "t1");

        // Ensure no ambient Activity
        Activity.Current = null;

        var token = issuer.IssueFor(principal);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "trace_id");
    }

    [Fact]
    public void IssueFor_EmitTraceIdClaimOff_SuppressesEvenWithActivity()
    {
        var issuer = BuildIssuer(emitTraceId: false, hasHttpContext: true);
        var principal = BuildPrincipal("u1", "t1");

        using var activity = new Activity("test").Start();

        var token = issuer.IssueFor(principal);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "trace_id");
    }

    [Fact]
    public void IssueFor_DoesNotBreakStrictDownstream_WhenTraceIdEmitted()
    {
        // C3 guard test: a strict downstream validator (signature + issuer +
        // audience + lifetime) must still accept a token that carries the
        // additional trace_id claim.
        var issuer = BuildIssuer(emitTraceId: true, hasHttpContext: true);
        var principal = BuildPrincipal("u1", "t1");

        using var activity = new Activity("test").Start();

        var token = issuer.IssueFor(principal);

        var validation = BuildStrictValidationParams();
        var handler = new JwtSecurityTokenHandler();
        var validated = handler.ValidateToken(token, validation, out _);
        validated.Should().NotBeNull();
    }

    // --- helpers ----------------------------------------------------------

    private S2sTokenIssuer BuildIssuer(bool emitTraceId, bool hasHttpContext)
    {
        var options = Options.Create(new KunGatewayOptions
        {
            ExternalIssuer = "https://login.example.com",
            ExternalAudience = "my-api",
            ExternalJwksUri = "https://login.example.com/.well-known/openid-configuration",
            SigningKeyPath = _privateKeyPath,
            PublicKeyPath = _publicKeyPath,
            EmitTraceIdClaim = emitTraceId,
        });

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(hasHttpContext ? new DefaultHttpContext() : null);

        return new S2sTokenIssuer(options, CreateDevEnv(), accessor);
    }

    private TokenValidationParameters BuildStrictValidationParams()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(_publicKeyPath));
        var key = new RsaSecurityKey(rsa);
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "kun-gateway",
            ValidateAudience = true,
            ValidAudience = "kun-internal",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = ["RS256"],
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromSeconds(5),
        };
    }

    private static ClaimsPrincipal BuildPrincipal(string sub, string? tenantId)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, sub) };
        if (tenantId is not null) claims.Add(new Claim("tenant_id", tenantId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static IHostEnvironment CreateDevEnv()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        return env;
    }
}
