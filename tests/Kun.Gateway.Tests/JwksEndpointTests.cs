using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Kun.Gateway.Configuration;
using Kun.Gateway.Endpoints;
using Kun.Gateway.Jwks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kun.Gateway.Tests;

public sealed class JwksEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapKunJwks_AnonymousRequest_RespectsGateAndUsesSourceGeneratedJson(bool blocked)
    {
        // 独立映射仍支持仅配置公开 PEM，不要求 AddKunGateway 或私钥。
        using var key = RSA.Create(2048);
        var path = Path.Combine(Path.GetTempPath(), $"kun-public-jwks-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(path, key.ExportSubjectPublicKeyInfoPem(), TestContext.Current.CancellationToken);
        try
        {
            using var host = await new HostBuilder().ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication();
                    services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                    services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine());
                    services.AddSingleton<JwksResponseBuilder>();
                    services.Configure<KunGatewayOptions>(options => options.PublicKeyPath = path);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapKunJwks(context => !context.Request.Query.ContainsKey("blocked")));
                });
            }).StartAsync(TestContext.Current.CancellationToken);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync("/.well-known/jwks.json" + (blocked ? "?blocked" : ""), TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(blocked ? HttpStatusCode.NotFound : HttpStatusCode.OK);
            if (!blocked)
            {
                response.Headers.CacheControl!.NoStore.Should().BeTrue();
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                var keys = body.RootElement.GetProperty("keys").EnumerateArray().ToArray();
                keys.Should().ContainSingle();
                keys[0].GetProperty("kty").GetString().Should().Be("RSA");
                keys[0].GetProperty("kid").GetString().Should().NotBeNullOrEmpty();
                keys[0].TryGetProperty("d", out _).Should().BeFalse();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
