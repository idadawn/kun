using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Kun.Core.MultiTenancy;
using Kun.MultiTenancy.DependencyInjection;
using Kun.MultiTenancy.Middleware;
using Kun.Security.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kun.IntegrationTests.Security;

public sealed class ProductionTenantTests
{
    [Theory]
    [InlineData("none", HttpStatusCode.Unauthorized)]
    [InlineData("malformed", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-key", HttpStatusCode.Unauthorized)]
    [InlineData("missing-claim", HttpStatusCode.Unauthorized)]
    [InlineData("legacy-claim", HttpStatusCode.Unauthorized)]
    [InlineData("unsafe-claim", HttpStatusCode.Unauthorized)]
    [InlineData("duplicate-claim", HttpStatusCode.Unauthorized)]
    [InlineData("valid", HttpStatusCode.OK)]
    public async Task Production_RequiresValidatedCanonicalTenant(string scenario, HttpStatusCode expected)
    {
        using var key = RSA.Create(2048);
        using var wrongKey = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(key);
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddKunSecurity();
                services.AddKunMultiTenancy(options =>
                {
                    options.Production = true;
                    options.UseHttpHeader("X-Tenant-Id");
                    options.FallbackTenantId = "default";
                });
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                {
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = "tenant-tests",
                        ValidAudience = "consumer",
                        IssuerSigningKey = signingKey,
                        ClockSkew = TimeSpan.Zero
                    };
                });
            }).Configure(app =>
            {
                app.UseAuthentication();
                app.UseKunMultiTenancy();
                app.Run(ctx => ctx.Response.WriteAsync(
                    ctx.RequestServices.GetRequiredService<ITenantContext>().TenantId ?? "missing", ctx.RequestAborted));
            })).StartAsync(TestContext.Current.CancellationToken);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");
        request.Headers.Add("X-Tenant-Id", "spoofed");
        if (scenario != "none")
        {
            var claims = new List<Claim>();
            if (scenario != "missing-claim")
                claims.Add(new Claim(scenario == "legacy-claim" ? "tid" : "tenant_id",
                    scenario == "unsafe-claim" ? "tenant-a\n" : "tenant-a"));
            if (scenario == "duplicate-claim") claims.Add(new Claim("tenant_id", "tenant-b"));
            var token = new JwtSecurityToken("tenant-tests", "consumer", claims,
                DateTime.UtcNow.AddMinutes(-5),
                scenario == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(1),
                new SigningCredentials(scenario == "wrong-key" ? new RsaSecurityKey(wrongKey) : signingKey,
                    SecurityAlgorithms.RsaSha256));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                scenario == "malformed" ? "invalid" : new JwtSecurityTokenHandler().WriteToken(token));
        }
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
            Assert.Equal("tenant-a", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
