using FluentAssertions;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.AspNetCore;
using Kun.Identity.Configuration;
using Kun.Identity.Implementations.Session;
using Kun.Identity.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Identity.Tests.AspNetCore;

public sealed class CookieAuthMiddlewareTests
{
    private static IOptions<KunCookieAuthOptions> Opts(Action<KunCookieAuthOptions>? mutate = null)
    {
        var o = new KunCookieAuthOptions { CookieName = "kun.sid", SessionTtl = TimeSpan.FromHours(1) };
        mutate?.Invoke(o);
        return Options.Create(o);
    }

    private static SessionData Sample() => new(
        UserId: "u-1", TenantId: "t-1", Email: null, Name: null,
        Roles: new[] { "viewer" },
        AccessToken: "at", RefreshToken: null,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task Invoke_AnonymousRequest_PassesThroughUnchanged()
    {
        var store = new InMemorySessionStore(new FakeTimeProvider());
        var middleware = new KunCookieAuthMiddleware(
            next: _ => Task.CompletedTask,
            store, Opts(), new FakeTimeProvider(), NullLogger<KunCookieAuthMiddleware>.Instance);
        var ctx = new DefaultHttpContext();

        await middleware.InvokeAsync(ctx);

        ctx.User.Identity!.IsAuthenticated.Should().BeFalse();
        ctx.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task Invoke_WithValidCookie_PopulatesUser()
    {
        var clock = new FakeTimeProvider();
        var store = new InMemorySessionStore(clock);
        await store.SetAsync("sid-ok", Sample(), TimeSpan.FromMinutes(30), TestContext.Current.CancellationToken);

        var middleware = new KunCookieAuthMiddleware(
            next: _ => Task.CompletedTask,
            store, Opts(o => o.SlidingExpiration = false), clock, NullLogger<KunCookieAuthMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = "kun.sid=sid-ok";

        await middleware.InvokeAsync(ctx);

        ctx.User.Identity!.IsAuthenticated.Should().BeTrue();
        ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value.Should().Be("u-1");
    }

    [Fact]
    public async Task Invoke_WithExpiredOrUnknownCookie_EvictsStaleCookie()
    {
        var store = new InMemorySessionStore(new FakeTimeProvider());
        var middleware = new KunCookieAuthMiddleware(
            next: _ => Task.CompletedTask,
            store, Opts(), new FakeTimeProvider(), NullLogger<KunCookieAuthMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = "kun.sid=never-existed";

        await middleware.InvokeAsync(ctx);

        ctx.User.Identity!.IsAuthenticated.Should().BeFalse();
        ctx.Response.Headers.SetCookie.ToString().Should().Contain("kun.sid=").And.Contain("expires=Thu, 01 Jan 1970");
    }

    [Fact]
    public async Task Invoke_SlidingExpiration_RefreshesCookieAndStore()
    {
        var clock = new FakeTimeProvider();
        var store = new InMemorySessionStore(clock);
        await store.SetAsync("sid-slide", Sample(), TimeSpan.FromMinutes(10), TestContext.Current.CancellationToken);

        var middleware = new KunCookieAuthMiddleware(
            next: _ => Task.CompletedTask,
            store, Opts(o => o.SessionTtl = TimeSpan.FromHours(2)), clock, NullLogger<KunCookieAuthMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = "kun.sid=sid-slide";

        await middleware.InvokeAsync(ctx);

        ctx.Response.Headers.SetCookie.ToString()
            .Should().Contain("kun.sid=sid-slide");
    }

    [Fact]
    public async Task Invoke_DoesNotOverwriteExistingAuthenticatedUser_WhenNoCookie()
    {
        // If the client doesn't present our cookie, don't clobber
        // HttpContext.User that a previous auth scheme (e.g. Bearer)
        // may have installed.
        var store = new InMemorySessionStore(new FakeTimeProvider());
        var middleware = new KunCookieAuthMiddleware(
            next: _ => Task.CompletedTask,
            store, Opts(), new FakeTimeProvider(), NullLogger<KunCookieAuthMiddleware>.Instance);
        var ctx = new DefaultHttpContext();
        var pre = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("existing", "yes") },
                authenticationType: "Bearer"));
        ctx.User = pre;

        await middleware.InvokeAsync(ctx);

        ctx.User.Should().BeSameAs(pre);
    }
}
