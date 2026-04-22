extern alias CatalogApp;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Kun.Sample.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kun.Sample.Tests;

/// <summary>
/// Shared fixture: one Postgres container + one Catalog WebApplicationFactory.
/// </summary>
public sealed class CatalogWebFactory : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private WebApplicationFactory<CatalogApp::Program>? _factory;

    /// <summary>Gets the configured HttpClient for the Catalog service.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();

        var cs = _postgres.ConnectionString;

        // OTel SDK reads OTEL_SDK_DISABLED via Environment.GetEnvironmentVariable,
        // not via IConfiguration — set it as a real env var before WAF builds.
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "");

        _factory = new WebApplicationFactory<CatalogApp::Program>()
            .WithWebHostBuilder(builder =>
            {
                // ConfigureAppConfiguration runs before Program.cs reads configuration,
                // so in-memory overrides take effect for builder.Configuration inside Program.
                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:CatalogDb"] = cs,
                        ["ConnectionStrings:CatalogDbRead"] = cs,
                    });
                });
                builder.UseSetting("ConnectionStrings:CatalogDb", cs);
                builder.UseSetting("ConnectionStrings:CatalogDbRead", cs);
            });

        // Trigger app startup eagerly so any startup exception surfaces here.
        Client = _factory.CreateClient();

        // Verify the app started correctly via the health endpoint.
        var health = await Client.GetAsync("/health");
        if (!health.IsSuccessStatusCode)
        {
            var body = await health.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"App startup failed, /health returned {health.StatusCode}: {body}");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>
/// End-to-end tests for the Catalog sample service.
/// </summary>
public sealed class CatalogApiTests : IClassFixture<CatalogWebFactory>
{
    private readonly HttpClient _client;

    /// <summary>Injects the shared factory fixture.</summary>
    public CatalogApiTests(CatalogWebFactory factory)
    {
        _client = factory.Client;
    }

    /// <summary>
    /// POST /products with a valid body returns 200 and a product id;
    /// GET /products/{id} returns the created product.
    /// </summary>
    [Fact]
    public async Task PostProduct_ThenGetById_ReturnsProduct()
    {
        var ct = TestContext.Current.CancellationToken;

        var payload = new
        {
            name = "Test Widget",
            description = "A widget for testing",
            price = 9.99m,
            categoryId = Guid.NewGuid(),
            initialStock = 10,
        };

        var postResponse = await _client.PostAsJsonAsync("/products", payload, ct);
        var rawError = await postResponse.Content.ReadAsStringAsync(ct);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"a valid CreateProductCommand should be accepted; server said: {rawError}");

        var productId = JsonSerializer.Deserialize<Guid>(rawError);
        productId.Should().NotBeEmpty();

        var getResponse = await _client.GetAsync($"/products/{productId}", ct);
        var getBody = await getResponse.Content.ReadAsStringAsync(ct);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"GET /products/{{id}} should return the created product; server said: {getBody}");

        getBody.Should().Contain("Test Widget");
    }

    /// <summary>
    /// GET /openapi/v1.json returns a well-formed document listing the registered
    /// routes. Serves as the P1-7 smoke test — catches regressions where someone
    /// drops <c>AddOpenApi</c>/<c>MapOpenApi</c> or breaks the Minimal-API metadata
    /// pipeline that feeds the document.
    /// </summary>
    [Fact]
    public async Task OpenApi_Document_Is_Served_And_Lists_Registered_Paths()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/openapi/v1.json", ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"Development environment should expose the OpenAPI document; body: {await response.Content.ReadAsStringAsync(ct)}");

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.TryGetProperty("openapi", out var openapiVersion).Should().BeTrue();
        openapiVersion.GetString().Should().StartWith("3.",
            because: "Kun services default to OAS 3.x via Microsoft.AspNetCore.OpenApi");

        doc.RootElement.TryGetProperty("paths", out var paths).Should().BeTrue();
        paths.EnumerateObject().Should().NotBeEmpty(
            because: "the catalog service registers at least /products and /products/{id}");

        var pathNames = paths.EnumerateObject().Select(p => p.Name).ToHashSet();
        pathNames.Should().Contain("/products");
    }

    /// <summary>
    /// POST /products with an empty name returns 400 (validation failure).
    /// </summary>
    [Fact]
    public async Task PostProduct_EmptyName_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;

        var payload = new
        {
            name = "",
            description = "desc",
            price = 5.00m,
            categoryId = Guid.NewGuid(),
            initialStock = 0,
        };

        var response = await _client.PostAsJsonAsync("/products", payload, ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            because: "an empty product name should fail validation");
    }
}
