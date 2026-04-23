using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Kun.RateLimit.Abstractions;
using Kun.RateLimit.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kun.RateLimit.Tests;

public sealed class KunRateLimitTests
{
    private const string TenantHeader = "X-Test-Tenant";
    private const string IpHeader = "X-Forwarded-For";

    [Fact]
    public async Task WithinLimit_Returns200()
    {
        using var host = await StartApp(opts => opts.AddWindow("api", w =>
        {
            w.PermitLimit = 3;
            w.Period = TimeSpan.FromMinutes(1);
            w.PartitionKey = RateLimitPartitionKey.Tenant;
        }));
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExceedingLimit_Returns429WithRetryAfter()
    {
        using var host = await StartApp(opts => opts.AddWindow("api", w =>
        {
            w.PermitLimit = 2;
            w.Period = TimeSpan.FromMinutes(1);
            w.PartitionKey = RateLimitPartitionKey.Tenant;
        }));
        var client = host.GetTestClient();
        var tenant = Guid.NewGuid().ToString("N");

        await SendAs(client, tenant, "/api");
        await SendAs(client, tenant, "/api");
        var blocked = await SendAs(client, tenant, "/api");

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.Should().ContainSingle(h => h.Key == "Retry-After");
        blocked.Headers.GetValues("Retry-After").Single().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TwoTenants_HaveIndependentBuckets()
    {
        using var host = await StartApp(opts => opts.AddWindow("api", w =>
        {
            w.PermitLimit = 2;
            w.Period = TimeSpan.FromMinutes(1);
            w.PartitionKey = RateLimitPartitionKey.Tenant;
        }));
        var client = host.GetTestClient();

        await SendAs(client, "tenant-a", "/api");
        await SendAs(client, "tenant-a", "/api");
        var tenantABlocked = await SendAs(client, "tenant-a", "/api");

        var tenantBHit1 = await SendAs(client, "tenant-b", "/api");
        var tenantBHit2 = await SendAs(client, "tenant-b", "/api");

        tenantABlocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        tenantBHit1.StatusCode.Should().Be(HttpStatusCode.OK);
        tenantBHit2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WindowReset_ClearsCounter()
    {
        using var host = await StartApp(opts => opts.AddWindow("api", w =>
        {
            w.PermitLimit = 1;
            w.Period = TimeSpan.FromMilliseconds(300);
            w.PartitionKey = RateLimitPartitionKey.Tenant;
            w.AutoReplenishment = true;
        }));
        var client = host.GetTestClient();
        var tenant = Guid.NewGuid().ToString("N");

        await SendAs(client, tenant, "/api");
        var blocked = await SendAs(client, tenant, "/api");
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        await Task.Delay(700, TestContext.Current.CancellationToken);

        var afterReset = await SendAs(client, tenant, "/api");
        afterReset.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthWindow_AppliesStricterLimit_IndependentFromApi()
    {
        using var host = await StartApp(opts =>
        {
            opts.AddWindow("api", w =>
            {
                w.PermitLimit = 10;
                w.Period = TimeSpan.FromMinutes(1);
                w.PartitionKey = RateLimitPartitionKey.Tenant;
            });
            opts.AddWindow("auth", w =>
            {
                w.PermitLimit = 2;
                w.Period = TimeSpan.FromMinutes(1);
                w.PartitionKey = RateLimitPartitionKey.Ip;
            });
        });
        var client = host.GetTestClient();

        await SendAs(client, tenant: "x", "/auth", ip: "10.0.0.1");
        await SendAs(client, tenant: "x", "/auth", ip: "10.0.0.1");
        var authBlocked = await SendAs(client, tenant: "x", "/auth", ip: "10.0.0.1");

        // The api policy partitions on tenant and has a higher limit — it stays open for the same ip.
        var apiStillOpen = await SendAs(client, tenant: "x", "/api", ip: "10.0.0.1");

        authBlocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        apiStillOpen.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OnRejectedHook_IsInvoked()
    {
        var rejections = 0;
        using var host = await StartApp(opts =>
        {
            opts.AddWindow("api", w =>
            {
                w.PermitLimit = 1;
                w.Period = TimeSpan.FromMinutes(1);
                w.PartitionKey = RateLimitPartitionKey.Tenant;
            });
            opts.OnRejected = (_, _) =>
            {
                Interlocked.Increment(ref rejections);
                return ValueTask.CompletedTask;
            };
        });
        var client = host.GetTestClient();

        await SendAs(client, "t", "/api");
        await SendAs(client, "t", "/api");
        await SendAs(client, "t", "/api");

        rejections.Should().BeGreaterThanOrEqualTo(1);
    }

    private static async Task<HttpResponseMessage> SendAs(
        HttpClient client,
        string tenant,
        string path,
        string? ip = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add(TenantHeader, tenant);
        if (ip is not null)
        {
            req.Headers.Add(IpHeader, ip);
        }
        return await client.SendAsync(req, TestContext.Current.CancellationToken);
    }

    private static async Task<IHost> StartApp(Action<KunRateLimitOptions> configure)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddKunRateLimit(configure);
                services.AddRouting();
            });
            web.Configure(app =>
            {
                app.Use(async (ctx, next) =>
                {
                    if (ctx.Request.Headers.TryGetValue(TenantHeader, out var tenant))
                    {
                        var identity = new ClaimsIdentity("test");
                        identity.AddClaim(new Claim("tenant_id", tenant.ToString()));
                        identity.AddClaim(new Claim("sub", "user-" + tenant));
                        ctx.User = new ClaimsPrincipal(identity);
                    }
                    await next(ctx);
                });
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/api", () => Results.Ok("api")).RequireRateLimiting("api");
                    endpoints.MapGet("/auth", () => Results.Ok("auth")).RequireRateLimiting("auth");
                });
            });
        });

        var host = await builder.StartAsync();
        return host;
    }
}
