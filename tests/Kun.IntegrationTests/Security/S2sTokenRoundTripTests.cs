using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Kun.Gateway;
using Kun.Gateway.Configuration;
using Kun.Security.Authentication;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.IntegrationTests.Security;

/// <summary>
/// Contract tests: a token minted by <see cref="S2sTokenIssuer"/> must validate correctly
/// via the <see cref="KunServiceAuthenticationOptions"/>-derived <see cref="TokenValidationParameters"/>.
/// </summary>
public sealed class S2sTokenRoundTripTests : IDisposable
{
    private const string InternalIssuer = "kun-gateway";
    private const string InternalAudience = "kun-internal";

    private readonly RSA _rsa;
    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;
    private readonly S2sTokenIssuer _issuer;
    private readonly RsaSecurityKey _publicKey;

    public S2sTokenRoundTripTests()
    {
        _rsa = RSA.Create(2048);

        _privateKeyPath = Path.GetTempFileName();
        _publicKeyPath = Path.GetTempFileName();
        File.WriteAllText(_privateKeyPath, _rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_publicKeyPath, _rsa.ExportRSAPublicKeyPem());

        var options = Options.Create(new KunGatewayOptions
        {
            ExternalIssuer = "https://login.example.com",
            ExternalAudience = "my-api",
            ExternalJwksUri = "https://login.example.com/.well-known/openid-configuration",
            SigningKeyPath = _privateKeyPath,
            PublicKeyPath = _publicKeyPath,
        });

        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        _issuer = new S2sTokenIssuer(options, env);

        var rsaValidation = RSA.Create();
        rsaValidation.ImportFromPem(File.ReadAllText(_publicKeyPath));
        _publicKey = new RsaSecurityKey(rsaValidation);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);
    }

    private static TokenValidationParameters BuildValidationParams(IEnumerable<SecurityKey> keys)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = InternalIssuer,
            ValidateAudience = true,
            ValidAudience = InternalAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = ["RS256", "ES256"],
            IssuerSigningKeys = keys.ToList(),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    private static ClaimsPrincipal BuildPrincipal(string sub, string tenantId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, sub),
            new("tenant_id", tenantId),
        };
        foreach (var role in roles)
            claims.Add(new Claim("roles", role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void MintedToken_ValidatesWithGatewayPublicKey()
    {
        // Arrange
        var principal = BuildPrincipal("user-123", "tenant-abc", "admin", "reader");
        var tvp = BuildValidationParams([_publicKey]);
        var handler = new JwtSecurityTokenHandler();

        // Act
        var token = _issuer.IssueFor(principal);
        var validated = handler.ValidateToken(token, tvp, out _);

        // Assert — claims flow through
        validated.Should().NotBeNull();

        var jwt = handler.ReadJwtToken(token);

        // sub claim
        var sub = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);
        sub.Should().NotBeNull();
        sub!.Value.Should().Be("user-123");

        // tenant_id claim
        var tenantClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id");
        tenantClaim.Should().NotBeNull();
        tenantClaim!.Value.Should().Be("tenant-abc");

        // roles claims
        var roles = jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value).ToList();
        roles.Should().Contain("admin").And.Contain("reader");

        // aud == "kun-internal"
        jwt.Audiences.Should().Contain(InternalAudience);

        // iss == KunGatewayOptions.InternalIssuer default
        jwt.Issuer.Should().Be(InternalIssuer);
    }

    [Fact]
    public void TokenSignedWithDifferentKey_FailsValidation()
    {
        // Arrange — mint with the gateway key, but validate against a different public key
        var principal = BuildPrincipal("user-456", "tenant-xyz");
        var token = _issuer.IssueFor(principal);

        // Generate a completely different RSA key for validation
        using var wrongRsa = RSA.Create(2048);
        var wrongKey = new RsaSecurityKey(wrongRsa);
        var tvp = BuildValidationParams([wrongKey]);

        var handler = new JwtSecurityTokenHandler();

        // Act + Assert — signature won't match the wrong public key
        var act = () => handler.ValidateToken(token, tvp, out _);
        act.Should().Throw<SecurityTokenSignatureKeyNotFoundException>();
    }
}

internal sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "test";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
