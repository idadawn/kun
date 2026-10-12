using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Kun.AspNetCore.Authorization;
using Kun.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Kun.IntegrationTests.Authorization;

public sealed class PrincipalKindEndpointTests
{
    [Fact]
    public async Task SignedServiceAndUser_WithSameSubjectId_ReceiveDifferentPermissionDecisions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var signing = RSA.Create(2048);
        var key = new RsaSecurityKey(signing) { KeyId = "principal-kind-test" };
        var seen = new List<AuthorizationSubject>(); var executed = 0;
        var checker = Substitute.For<IPermissionChecker>();
#pragma warning disable CA2012 // 判定端口只授予 user 元组；测试真实 HTTP PEP 是否保留签名身份类型。
        checker.CheckAsync(Arg.Any<PermissionRequest>(), Arg.Any<CancellationToken>()).Returns(info =>
        {
            var subject = info.Arg<PermissionRequest>().Subject; seen.Add(subject);
            return ValueTask.FromResult(new PermissionDecision(subject == new AuthorizationSubject("same-id", "tenant-a", "user"), "user-only-grant", []));
        });
#pragma warning restore CA2012
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(checker);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "principal-kind-test",
                ValidateAudience = true,
                ValidAudience = "permission-endpoint",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            };
        });
        builder.Services.AddKunAuthorization();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/protected", () => { executed++; return "ok"; }).RequireKunPermission("sample:item:read");
        await app.StartAsync(ct); using var client = app.GetTestClient();
        foreach (var (kind, status) in new[] { ("user", HttpStatusCode.OK), ("service", HttpStatusCode.Forbidden), ("other", HttpStatusCode.Forbidden) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "principal-kind-test",
                Audience = "permission-endpoint",
                IssuedAt = DateTime.UtcNow.AddSeconds(-1),
                NotBefore = DateTime.UtcNow.AddSeconds(-1),
                Expires = DateTime.UtcNow.AddMinutes(2),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
                Claims = new Dictionary<string, object> { ["sub"] = "same-id", ["tenant_id"] = "tenant-a", ["principal_kind"] = kind }
            });
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct); Assert.Equal(status, response.StatusCode);
        }
        Assert.Equal(1, executed); Assert.Equal(["user", "service"], seen.Select(subject => subject.Kind));
        Assert.All(seen, subject => Assert.Equal("same-id", subject.Id));
    }
}
