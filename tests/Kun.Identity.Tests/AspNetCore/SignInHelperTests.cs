using System.Security.Claims;
using FluentAssertions;
using Kun.Identity.Abstractions;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.AspNetCore;
using Kun.Identity.Configuration;
using Kun.Identity.Implementations.Session;
using Kun.Identity.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kun.Identity.Tests.AspNetCore;

public sealed class SignInHelperTests
{
    private static IOptions<KunCookieAuthOptions> Opts(Action<KunCookieAuthOptions>? mutate = null)
    {
        var o = new KunCookieAuthOptions { CookieName = "kun.sid", SessionTtl = TimeSpan.FromHours(1) };
        mutate?.Invoke(o);
        return Options.Create(o);
    }

    [Fact]
    public async Task SignInAsync_WithSessionData_PersistsAndEmitsCookie()
    {
        var clock = new FakeTimeProvider();
        var store = new InMemorySessionStore(clock);
        var helper = new SignInHelper(store, new AlwaysValidator(), Opts(), clock);
        var ctx = new DefaultHttpContext();
        var data = BuildSample();
        var ct = TestContext.Current.CancellationToken;

        var sid = await helper.SignInAsync(ctx, data, ct);

        sid.Should().NotBeNullOrWhiteSpace();
        (await store.GetAsync(sid, ct)).Should().NotBeNull();
        ctx.Response.Headers.SetCookie.ToString().Should().Contain("kun.sid=").And.Contain(sid);
    }

    [Fact]
    public async Task SignInAsync_WithOidcTokens_ValidatesIdTokenBeforeTrustingClaims()
    {
        var clock = new FakeTimeProvider();
        var store = new InMemorySessionStore(clock);
        var helper = new SignInHelper(store, new AlwaysValidator(), Opts(), clock);
        var tokens = new OidcTokenResponse(
            AccessToken: "at",
            IdToken: "eyJfake.id.token",
            RefreshToken: "rt",
            TokenType: "Bearer",
            ExpiresIn: 3600);
        var ctx = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var sid = await helper.SignInAsync(ctx, tokens, ct);

        var stored = await store.GetAsync(sid, ct);
        stored.Should().NotBeNull();
        stored!.UserId.Should().Be("user-from-id-token");
    }

    [Fact]
    public async Task SignInAsync_Throws_OnMissingIdToken()
    {
        var helper = new SignInHelper(new InMemorySessionStore(), new AlwaysValidator(), Opts(), TimeProvider.System);
        var tokens = new OidcTokenResponse("at", IdToken: null, RefreshToken: null, TokenType: "Bearer", ExpiresIn: 3600);
        var ctx = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var act = () => helper.SignInAsync(ctx, tokens, ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("id-token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SignInAsync_Throws_OnIdTokenValidationFailure()
    {
        var helper = new SignInHelper(new InMemorySessionStore(), new AlwaysInvalidValidator(), Opts(), TimeProvider.System);
        var tokens = new OidcTokenResponse("at", "fake.id.token", "rt", "Bearer", 3600);
        var ctx = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var act = () => helper.SignInAsync(ctx, tokens, ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("failed validation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SignOutAsync_DeletesStoreEntryAndCookie()
    {
        var clock = new FakeTimeProvider();
        var store = new InMemorySessionStore(clock);
        var helper = new SignInHelper(store, new AlwaysValidator(), Opts(), clock);
        var ctx = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var sid = await helper.SignInAsync(ctx, BuildSample(), ct);
        // Replay the sid as an incoming cookie so SignOut has something to clear.
        var newCtx = new DefaultHttpContext();
        newCtx.Request.Headers.Cookie = $"kun.sid={sid}";

        await helper.SignOutAsync(newCtx, ct);

        (await store.GetAsync(sid, ct)).Should().BeNull();
        newCtx.Response.Headers.SetCookie.ToString().Should().Contain("kun.sid=").And.Contain("expires=Thu, 01 Jan 1970");
    }

    [Fact]
    public void BuildSessionData_ProjectsClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "u"),
            new Claim(KunCurrentUser.TenantIdClaimType, "t"),
            new Claim(ClaimTypes.Email, "e@x"),
            new Claim(ClaimTypes.Name, "Name"),
            new Claim(ClaimTypes.Role, "r1"),
        }, authenticationType: "test"));
        var tokens = new OidcTokenResponse("at", "id", "rt", "Bearer", 120);
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var data = SignInHelper.BuildSessionData(principal, tokens, now);

        data.UserId.Should().Be("u");
        data.TenantId.Should().Be("t");
        data.Email.Should().Be("e@x");
        data.Name.Should().Be("Name");
        data.Roles.Should().Equal("r1");
        data.AccessToken.Should().Be("at");
        data.RefreshToken.Should().Be("rt");
        data.ExpiresAt.Should().Be(now.AddSeconds(120));
    }

    private static SessionData BuildSample() => new(
        UserId: "user-x",
        TenantId: "tenant-y",
        Email: null,
        Name: null,
        Roles: Array.Empty<string>(),
        AccessToken: "at",
        RefreshToken: null,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

    private sealed class AlwaysValidator : IJwtValidator
    {
        public Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken ct = default)
        {
            var id = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-from-id-token"),
                new Claim(ClaimTypes.Name, "From Id Token"),
            }, authenticationType: "test");
            return Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(id));
        }
    }

    private sealed class AlwaysInvalidValidator : IJwtValidator
    {
        public Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken ct = default)
            => Task.FromResult<ClaimsPrincipal?>(null);
    }
}
