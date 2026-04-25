using System.Net;
using System.Net.Http.Json;
using Kun.Gateway.Configuration;
using Kun.Gateway.Jwks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kun.Gateway.Tests;

public sealed class KunInternalJwksEndpointTests : IAsyncLifetime
{
    private string _publicKeyPath = "";

    public ValueTask InitializeAsync()
    {
        // Generate a throwaway RSA key PEM so the endpoint has something to read. The tests
        // only verify the HTTP gating, not the cryptographic shape of the response.
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"kun-test-jwks-{Guid.NewGuid():N}.pem");
        File.WriteAllText(_publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_publicKeyPath))
        {
            File.Delete(_publicKeyPath);
        }
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Development_Environment_Returns_200_Even_Without_OptIn()
    {
        using var host = await BuildHostAsync(environment: "Development", exposeInternal: false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            KunInternalJwksEndpoint.DefaultPattern,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Production_Without_OptIn_Returns_404()
    {
        using var host = await BuildHostAsync(environment: "Production", exposeInternal: false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            KunInternalJwksEndpoint.DefaultPattern,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Production_With_OptIn_Returns_200()
    {
        using var host = await BuildHostAsync(environment: "Production", exposeInternal: true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            KunInternalJwksEndpoint.DefaultPattern,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Response_Body_Contains_Single_Signing_Key()
    {
        using var host = await BuildHostAsync(environment: "Development", exposeInternal: false);
        using var client = host.GetTestClient();

        var payload = await client.GetFromJsonAsync<JwksPayload>(
            KunInternalJwksEndpoint.DefaultPattern,
            TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        Assert.NotNull(payload!.Keys);
        Assert.Single(payload.Keys);
        var key = payload.Keys[0];
        Assert.Equal("RSA", key.Kty);
        Assert.Equal("sig", key.Use);
        Assert.Equal("RS256", key.Alg);
        Assert.False(string.IsNullOrEmpty(key.Kid));
        Assert.False(string.IsNullOrEmpty(key.N));
    }

    [Fact]
    public async Task Custom_Pattern_Is_Routable()
    {
        using var host = await BuildHostAsync(
            environment: "Development",
            exposeInternal: false,
            pattern: "/admin/jwks.json");
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            "/admin/jwks.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Additional_Gate_Returning_False_Yields_404()
    {
        using var host = await BuildHostAsync(
            environment: "Development",
            exposeInternal: false,
            additionalGate: _ => false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            KunInternalJwksEndpoint.DefaultPattern,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Additional_Gate_Returning_True_Allows_Through()
    {
        using var host = await BuildHostAsync(
            environment: "Development",
            exposeInternal: false,
            additionalGate: _ => true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            KunInternalJwksEndpoint.DefaultPattern,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<IHost> BuildHostAsync(
        string environment,
        bool exposeInternal,
        string pattern = KunInternalJwksEndpoint.DefaultPattern,
        Func<HttpContext, bool>? additionalGate = null)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseEnvironment(environment);
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<JwksResponseBuilder>();
                    services.Configure<KunGatewayOptions>(opts =>
                    {
                        opts.ExternalIssuer = "https://unused.example";
                        opts.ExternalAudience = "unused";
                        opts.ExternalJwksUri = "https://unused.example/.well-known/jwks.json";
                        opts.SigningKeyPath = _publicKeyPath;
                        opts.PublicKeyPath = _publicKeyPath;
                        opts.ExposeInternalJwks = exposeInternal;
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapKunInternalJwks(pattern, additionalGate));
                });
            });

        var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private sealed class JwksPayload
    {
        public List<JwkEntry> Keys { get; set; } = new();
    }

    private sealed class JwkEntry
    {
        public string? Kty { get; set; }
        public string? Use { get; set; }
        public string? Kid { get; set; }
        public string? Alg { get; set; }
        public string? N { get; set; }
        public string? E { get; set; }
    }
}
