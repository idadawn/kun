using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Kun.Core.MultiTenancy;
using Kun.MultiTenancy.Configuration;
using Kun.MultiTenancy.DependencyInjection;
using Kun.MultiTenancy.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kun.MultiTenancy.Tests;

public sealed class TenantContextMiddlewareTests
{
    [Fact]
    public async Task Middleware_PopulatesTenantContextFromJwtClaim()
    {
        using var host = await BuildHost(opts => opts.UseJwtClaim("tid"));
        var client = host.GetTestClient();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        req.Headers.Add("X-Test-Tenant-Claim", "tenant-a");
        var response = await client.SendAsync(req, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Be("tenant-a");
    }

    [Fact]
    public async Task Middleware_FallsBackToHeader_WhenClaimAbsent()
    {
        using var host = await BuildHost(opts => opts
            .UseJwtClaim("tid")
            .UseHttpHeader("X-Tenant-Id"));
        var client = host.GetTestClient();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        req.Headers.Add("X-Tenant-Id", "tenant-from-header");
        var response = await client.SendAsync(req, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Be("tenant-from-header");
    }

    [Fact]
    public async Task Middleware_Uses_FallbackTenantId_WhenNothingResolved()
    {
        using var host = await BuildHost(opts =>
        {
            opts.UseJwtClaim("tid");
            opts.UseHttpHeader("X-Tenant-Id");
            opts.FallbackTenantId = "default";
        });
        var client = host.GetTestClient();

        var body = await client.GetStringAsync("/whoami", TestContext.Current.CancellationToken);
        body.Should().Be("default");
    }

    private static async Task<IHost> BuildHost(Action<KunMultiTenancyOptions> configure)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddKunMultiTenancy(configure);
                services.AddRouting();
            });
            web.Configure(app =>
            {
                // Fake "auth": promote a header into a claim so the resolver sees it.
                app.Use(async (ctx, next) =>
                {
                    if (ctx.Request.Headers.TryGetValue("X-Test-Tenant-Claim", out var tid))
                    {
                        var identity = new ClaimsIdentity("test");
                        identity.AddClaim(new Claim("tid", tid.ToString()));
                        ctx.User = new ClaimsPrincipal(identity);
                    }
                    await next(ctx);
                });
                app.UseKunMultiTenancy();
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/whoami", (ITenantContext tenant) => Results.Content(tenant.TenantId ?? "<null>"));
                });
            });
        });
        return await builder.StartAsync();
    }
}
