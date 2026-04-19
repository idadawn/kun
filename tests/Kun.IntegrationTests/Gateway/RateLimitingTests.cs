#pragma warning disable ASPDEPR004, ASPDEPR008, IDE0005 // Tests deliberately use the simpler WebHostBuilder+TestServer pattern.
using FluentAssertions;
using Kun.Gateway.RateLimiting;
using Kun.Gateway.RateLimiting.Resolvers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using Xunit;

namespace Kun.IntegrationTests.Gateway;

/// <summary>
/// Contract tests for tenant-partitioned rate limiting.
/// Uses an in-process <see cref="TestServer"/> to avoid external dependencies.
/// The fixed-window limiter is configured with a low limit (3 req / 10 s) to trigger 429 quickly.
/// </summary>
public sealed class RateLimitingTests
{
    private const string PolicyName = "test-tenant-limit";
    private const int PermitLimit = 3;

    private static HttpClient BuildClient(IRateLimitPartitionResolver? resolver = null)
    {
        resolver ??= new TenantRateLimitPartitionResolver();

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(resolver);
                services.AddRateLimiter(options =>
                {
                    options.AddPolicy<string>(PolicyName, ctx =>
                    {
                        var key = resolver.ResolvePartitionKey(ctx);
                        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PermitLimit,
                            Window = TimeSpan.FromSeconds(10),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0,
                        });
                    });

                    options.OnRejected = static (ctx, _) =>
                    {
                        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        return ValueTask.CompletedTask;
                    };
                });

                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/api/test", () => Results.Ok("ok"))
                             .RequireRateLimiting(PolicyName);
                });
            });

        var server = new TestServer(builder);
        return server.CreateClient();
    }

    private static HttpRequestMessage BuildRequest(string? tenantId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        if (tenantId is not null)
            request.Headers.Add("X-Test-Tenant", tenantId);
        return request;
    }

    /// <summary>
    /// Sends <paramref name="count"/> sequential requests with the given tenant claim
    /// and returns the HTTP status codes.
    /// </summary>
    private static async Task<List<int>> SendRequestsAsync(
        HttpClient client, int count, string? tenantId)
    {
        var statuses = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            using var response = await client.SendAsync(BuildRequest(tenantId));
            statuses.Add((int)response.StatusCode);
        }
        return statuses;
    }

    [Fact]
    public async Task SameTenant_ExceedingLimit_Returns429()
    {
        // Arrange — resolver that reads the X-Test-Tenant header (simulates tenant claim)
        var resolver = new HeaderTenantResolver();
        using var client = BuildClient(resolver);

        // Act — send PermitLimit+2 requests for the same tenant
        var statuses = await SendRequestsAsync(client, PermitLimit + 2, "tenant-A");

        // Assert — first PermitLimit succeed, subsequent ones are throttled
        statuses.Take(PermitLimit).Should().AllSatisfy(s => s.Should().Be(200));
        statuses.Skip(PermitLimit).Should().AllSatisfy(s => s.Should().Be(429));
    }

    [Fact]
    public async Task DifferentTenants_BelowLimit_BothSucceed()
    {
        // Arrange — each tenant gets its own partition
        var resolver = new HeaderTenantResolver();
        using var client = BuildClient(resolver);

        // Act — each tenant sends exactly PermitLimit-1 requests (one below the limit)
        var statusesTenantA = await SendRequestsAsync(client, PermitLimit - 1, "tenant-B");
        var statusesTenantC = await SendRequestsAsync(client, PermitLimit - 1, "tenant-C");

        // Assert — both tenants stay within their own partition limit
        statusesTenantA.Should().AllSatisfy(s => s.Should().Be(200));
        statusesTenantC.Should().AllSatisfy(s => s.Should().Be(200));
    }

    /// <summary>
    /// Test-only resolver that reads the tenant key from the <c>X-Test-Tenant</c> request header.
    /// Avoids the need to set up JWT claims in the HttpContext.
    /// </summary>
    private sealed class HeaderTenantResolver : IRateLimitPartitionResolver
    {
        private const string AnonymousKey = "anonymous";

        public string ResolvePartitionKey(HttpContext context)
        {
            var tenantId = context.Request.Headers["X-Test-Tenant"].FirstOrDefault();
            return string.IsNullOrWhiteSpace(tenantId) ? AnonymousKey : tenantId;
        }
    }
}
