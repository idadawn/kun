using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Kun.Gateway.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.Gateway.Tests;

/// <summary>
/// Unit tests for <see cref="S2sTokenIssuer"/>.
/// Uses a temporary PEM key file generated in-test so no filesystem dependencies are required.
/// </summary>
public sealed class S2sTokenIssuerTests : IDisposable
{
    private const string InternalIssuer = "kun-gateway";
    private const string InternalAudience = "kun-internal";

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;
    private readonly RSA _rsa;
    private readonly S2sTokenIssuer _issuer;
    private readonly TokenValidationParameters _validationParams;

    public S2sTokenIssuerTests()
    {
        // Generate a fresh key pair for each test class
        _rsa = RSA.Create(2048);

        _privateKeyPath = Path.GetTempFileName();
        _publicKeyPath = Path.GetTempFileName();

        File.WriteAllText(_privateKeyPath, ExportPrivatePem(_rsa));
        File.WriteAllText(_publicKeyPath, ExportPublicPem(_rsa));

        var options = Options.Create(new KunGatewayOptions
        {
            ExternalIssuer = "https://login.example.com",
            ExternalAudience = "my-api",
            ExternalJwksUri = "https://login.example.com/.well-known/openid-configuration",
            SigningKeyPath = _privateKeyPath,
            PublicKeyPath = _publicKeyPath,
        });

        _issuer = new S2sTokenIssuer(options);

        // Build validation params using the public key
        var rsaForValidation = RSA.Create();
        rsaForValidation.ImportFromPem(File.ReadAllText(_publicKeyPath));
        var publicKey = new RsaSecurityKey(rsaForValidation);

        _validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = InternalIssuer,
            ValidateAudience = true,
            ValidAudience = InternalAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = ["RS256"],
            IssuerSigningKey = publicKey,
            ClockSkew = TimeSpan.FromSeconds(5),
        };
    }

    public void Dispose()
    {
        _rsa.Dispose();
        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);
    }

    private static ClaimsPrincipal BuildPrincipal(string sub, string? tenantId, params string[] roles)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, sub) };
        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId));
        foreach (var role in roles)
            claims.Add(new Claim("roles", role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void IssueFor_ValidPrincipal_ProducesValidToken()
    {
        // Arrange
        var principal = BuildPrincipal("user-123", "tenant-abc");

        // Act
        var token = _issuer.IssueFor(principal);

        // Assert — token must validate against our public key
        var handler = new JwtSecurityTokenHandler();
        var claimsPrincipal = handler.ValidateToken(token, _validationParams, out _);
        claimsPrincipal.Should().NotBeNull();
    }

    [Fact]
    public void IssueFor_CopiesSub_IntoToken()
    {
        // Arrange
        var principal = BuildPrincipal("user-xyz", "t1");

        // Act
        var token = _issuer.IssueFor(principal);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var sub = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);
        sub.Should().NotBeNull();
        sub!.Value.Should().Be("user-xyz");
    }

    [Fact]
    public void IssueFor_CopiesTenantId_IntoToken()
    {
        // Arrange
        var principal = BuildPrincipal("user-1", "tenant-99");

        // Act
        var token = _issuer.IssueFor(principal);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var tenantClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id");
        tenantClaim.Should().NotBeNull();
        tenantClaim!.Value.Should().Be("tenant-99");
    }

    [Fact]
    public void IssueFor_CopiesRoles_IntoToken()
    {
        // Arrange
        var principal = BuildPrincipal("user-1", "t1", "admin", "reader");

        // Act
        var token = _issuer.IssueFor(principal);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var roles = jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value).ToList();
        roles.Should().Contain("admin").And.Contain("reader");
    }

    [Fact]
    public void IssueFor_SetsCorrectAudience()
    {
        // Arrange
        var principal = BuildPrincipal("u1", null);

        // Act
        var token = _issuer.IssueFor(principal);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Audiences.Should().Contain(InternalAudience);
    }

    [Fact]
    public void IssueFor_TokenExpiry_IsWithinLifetime()
    {
        // Arrange
        var principal = BuildPrincipal("u1", "t1");
        var before = DateTime.UtcNow;

        // Act
        var token = _issuer.IssueFor(principal);
        var after = DateTime.UtcNow;

        // Assert — default lifetime is 60s
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.ValidTo.Should().BeAfter(before.AddSeconds(55));
        jwt.ValidTo.Should().BeBefore(after.AddSeconds(65));
    }

    private static string ExportPrivatePem(RSA rsa)
    {
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static string ExportPublicPem(RSA rsa)
    {
        return rsa.ExportRSAPublicKeyPem();
    }
}
