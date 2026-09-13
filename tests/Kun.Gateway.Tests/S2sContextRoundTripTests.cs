using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.Gateway.Configuration;
using Kun.Gateway.Identity;
using Kun.Gateway.Jwks;
using Kun.Security.Authentication;
using Kun.Security.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Kun.Gateway.Tests;

public sealed class S2sContextRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kun-s2s-context-" + Guid.NewGuid().ToString("N"));
    private readonly KunGatewayOptions _options;
    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();

    public S2sContextRoundTripTests()
    {
        Directory.CreateDirectory(_directory);
        using var key = RSA.Create(2048);
        _options = new KunGatewayOptions
        {
            ExternalIssuer = "external-identity",
            ExternalAudience = "external-app",
            ExternalJwksUri = "https://identity.invalid/jwks",
            SigningKeyPath = Path.Combine(_directory, "private.pem"),
            PublicKeyPath = Path.Combine(_directory, "public.pem"),
            ForwardedClaimTypes = ["sid", "identity_revision", "permission"]
        };
        File.WriteAllText(_options.SigningKeyPath, key.ExportRSAPrivateKeyPem());
        File.WriteAllText(_options.PublicKeyPath, key.ExportRSAPublicKeyPem());
        _environment.EnvironmentName.Returns("Production");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private S2sTokenIssuer Issuer() => new(Options.Create(_options), _environment, NoOpS2sPrincipalResolver.Instance);

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
    [
        new Claim("sub", "user.one"), new Claim("tenant_id", "tenant.one"),
        new Claim("role", "role.writer"), new Claim("role", "role.operator"), new Claim("roles", "role.operator"), new Claim(ClaimTypes.Role, "role.reader"),
        new Claim("sid", "session.one"), new Claim("identity_revision", "7", ClaimValueTypes.Integer64),
        new Claim("permission", "platform.system.manage"), new Claim("permission", "platform.application.manage"),
        new Claim("email", "private@example.invalid"), new Claim("exp", "9999999999", ClaimValueTypes.Integer64)
    ], "validated-external"));

    [Fact]
    public async Task GatewayToken_WhenValidatedByService_PreservesOnlyApprovedContext()
    {
        var issuer = Issuer();
        var token = await issuer.IssueForAsync(Principal(), TestContext.Current.CancellationToken);
        var document = new JwtSecurityTokenHandler().ReadJwtToken(token);
        document.Header.Kid.Should().Be(new JwksResponseBuilder().Build(_options.PublicKeyPath).Keys.Single().Kid);
        document.Id.Should().NotBeNullOrWhiteSpace();
        document.ValidTo.Should().BeBefore(DateTime.UtcNow.AddSeconds(65));
        document.Claims.Should().NotContain(claim => claim.Type == "email");

        using var publicRsa = RSA.Create();
        publicRsa.ImportFromPem(File.ReadAllText(_options.PublicKeyPath));
        var publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(publicRsa) { KeyId = document.Header.Kid });
        var keys = new JsonWebKeySet(); keys.Keys.Add(publicKey);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddKunSecurity();
        builder.Services.AddAuthentication("Bearer").AddKunServiceAuthentication(options => { options.Issuer = _options.InternalIssuer; options.StaticKeySet = keys; });
        builder.Services.Configure<JwtBearerOptions>("Bearer", options => options.MapInboundClaims = false);
        builder.Services.AddAuthorization();
        await using var service = builder.Build();
        service.UseAuthentication(); service.UseAuthorization();
        service.MapGet("/context", (ICurrentUser user, ITenantContext tenant, Microsoft.AspNetCore.Http.HttpContext context) =>
            string.Join('|', user.UserId, tenant.TenantId, string.Join(',', user.Roles.Order(StringComparer.Ordinal)),
                context.User.FindFirst("sid")?.Value, context.User.FindFirst("identity_revision")?.Value,
                string.Join(',', context.User.FindAll("permission").Select(claim => claim.Value).Order(StringComparer.Ordinal))))
            .RequireAuthorization();
        await service.StartAsync(TestContext.Current.CancellationToken);
        using var client = service.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        client.DefaultRequestHeaders.Add("X-User-Id", "user.forged");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "tenant.forged");

        var response = await client.GetAsync("/context", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be(
            "user.one|tenant.one|role.operator,role.reader,role.writer|session.one|7|platform.application.manage,platform.system.manage");
        _options.InternalAudience = "external-app";
        client.DefaultRequestHeaders.Authorization = new("Bearer", await Issuer().IssueForAsync(Principal(), TestContext.Current.CancellationToken));
        (await client.GetAsync("/context", TestContext.Current.CancellationToken)).StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IssueForAsync_WhenNoAllowlist_DropsApplicationClaims()
    {
        _options.ForwardedClaimTypes = [];
        var token = await Issuer().IssueForAsync(Principal(), TestContext.Current.CancellationToken);
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Should().NotContain(claim => claim.Type == "sid" || claim.Type == "identity_revision" || claim.Type == "permission" || claim.Type == "email");
    }

    [Theory]
    [InlineData("exp")]
    [InlineData("SUB")]
    [InlineData("tenant_id")]
    [InlineData("roles")]
    [InlineData("iss")]
    public void Constructor_WhenReservedClaimIsConfigured_RejectsOverride(string type)
    {
        _options.ForwardedClaimTypes = [type];
        var action = Issuer;
        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(301)]
    public async Task IssueForAsync_WhenLifetimeViolatesPolicy_RejectsIssuance(int seconds)
    {
        _options.InternalTokenLifetime = TimeSpan.FromSeconds(seconds);
        var action = () => Issuer().IssueForAsync(Principal(), TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IssueForAsync_WhenAlreadyCancelled_DoesNotMintToken()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var action = () => Issuer().IssueForAsync(Principal(), cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }
}
