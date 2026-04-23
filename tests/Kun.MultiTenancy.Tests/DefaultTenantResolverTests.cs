using System.Security.Claims;
using FluentAssertions;
using Kun.MultiTenancy.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.MultiTenancy.Tests;

public sealed class DefaultTenantResolverTests
{
    [Fact]
    public async Task JwtClaim_Wins_OverHeaderAndFallback()
    {
        var resolver = new DefaultTenantResolver(Options.Create(new KunMultiTenancyOptions()
            .UseJwtClaim("tid")
            .UseHttpHeader("X-Tenant-Id")));

        var ctx = BuildContext(claims: [new("tid", "tenant-from-claim")], headers: [("X-Tenant-Id", "tenant-from-header")]);

        var resolved = await resolver.ResolveTenantIdAsync(ctx);

        resolved.Should().Be("tenant-from-claim");
    }

    [Fact]
    public async Task HttpHeader_Wins_WhenClaimMissing()
    {
        var resolver = new DefaultTenantResolver(Options.Create(new KunMultiTenancyOptions()
            .UseJwtClaim("tid")
            .UseHttpHeader("X-Tenant-Id")));

        var ctx = BuildContext(claims: [], headers: [("X-Tenant-Id", "tenant-from-header")]);

        var resolved = await resolver.ResolveTenantIdAsync(ctx);

        resolved.Should().Be("tenant-from-header");
    }

    [Fact]
    public async Task Fallback_WhenBothSourcesEmpty()
    {
        var resolver = new DefaultTenantResolver(Options.Create(new KunMultiTenancyOptions
        {
            FallbackTenantId = "default",
        }.UseJwtClaim("tid").UseHttpHeader("X-Tenant-Id")));

        var ctx = BuildContext(claims: [], headers: []);

        var resolved = await resolver.ResolveTenantIdAsync(ctx);

        resolved.Should().Be("default");
    }

    [Fact]
    public async Task MultipleClaimNames_TriedInOrder()
    {
        var resolver = new DefaultTenantResolver(Options.Create(new KunMultiTenancyOptions()
            .UseJwtClaim("tid")
            .UseJwtClaim("tenant_id")));

        var ctx = BuildContext(claims: [new("tenant_id", "fallback-claim")], headers: []);

        var resolved = await resolver.ResolveTenantIdAsync(ctx);

        resolved.Should().Be("fallback-claim");
    }

    private static DefaultHttpContext BuildContext(IEnumerable<Claim> claims, IEnumerable<(string Key, string Value)> headers)
    {
        var ctx = new DefaultHttpContext();
        var identity = new ClaimsIdentity("test");
        foreach (var c in claims)
        {
            identity.AddClaim(c);
        }
        ctx.User = new ClaimsPrincipal(identity);
        foreach (var (k, v) in headers)
        {
            ctx.Request.Headers[k] = v;
        }
        return ctx;
    }
}
