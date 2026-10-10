using Kun.Identity.Abstractions;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.Implementations.Session;
using Kun.IntegrationTests.Fixtures;
using StackExchange.Redis;
using Xunit;

namespace Kun.IntegrationTests.Identity;

public sealed class SessionRegistryTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokeUser_AllSessionsGone_RenewCannotResurrect(bool distributed)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var prefix = "test:" + Guid.NewGuid().ToString("N") + ":";
        ISessionRegistry first = distributed ? new RedisSessionStore(connection, TimeProvider.System, prefix) : new InMemorySessionStore();
        ISessionRegistry second = distributed ? new RedisSessionStore(connection, TimeProvider.System, prefix) : first;
        var ct = TestContext.Current.CancellationToken;
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => first.SetAsync("session-" + index, Data("user-a"), TimeSpan.FromMinutes(2), ct)));
        await first.SetAsync("other-user", Data("user-b"), TimeSpan.FromMinutes(2), ct);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => second.RenewAsync("session-" + index, TimeSpan.FromMinutes(3), ct)));
        Assert.Equal(12, await first.RevokeUserAsync("user-a", ct));
        foreach (var index in Enumerable.Range(0, 12))
        {
            Assert.False(await second.RenewAsync("session-" + index, TimeSpan.FromMinutes(3), ct));
            Assert.Null(await first.GetAsync("session-" + index, ct));
        }
        Assert.NotNull(await second.GetAsync("other-user", ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_IsScopedToIssuerAndProviderSid_AndReplayDoesNotKillNewSession(bool distributed)
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        ISessionRegistry store = distributed ? new RedisSessionStore(connection, TimeProvider.System, "test:" + Guid.NewGuid().ToString("N") + ":") : new InMemorySessionStore();
        var ct = TestContext.Current.CancellationToken;
        await store.SetAsync("a", Data("user-a"), TimeSpan.FromMinutes(2), ct);
        await store.SetAsync("b", Data("user-a") with { ProviderSessionId = "another-device" }, TimeSpan.FromMinutes(2), ct);
        await store.SetAsync("c", Data("user-a") with { Issuer = "https://other.test" }, TimeSpan.FromMinutes(2), ct);
        var logout = new OidcLogoutToken("https://idp.test", "user-a", "provider-session", "logout-jti", DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.Equal(1, await store.ApplyLogoutAsync(logout, ct));
        Assert.Null(await store.GetAsync("a", ct)); Assert.NotNull(await store.GetAsync("b", ct)); Assert.NotNull(await store.GetAsync("c", ct));
        await store.SetAsync("new-login", Data("user-a"), TimeSpan.FromMinutes(2), ct);
        Assert.Equal(0, await store.ApplyLogoutAsync(logout, ct));
        Assert.NotNull(await store.GetAsync("new-login", ct));
        Assert.Equal(2, await store.ApplyLogoutAsync(logout with { SessionId = null, TokenId = "second-logout" }, ct));
        Assert.NotNull(await store.GetAsync("c", ct));
    }

    [Fact]
    public async Task RedisNonce_ConsumedExactlyOnceAcrossRegistryInstances()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var prefix = "test:" + Guid.NewGuid().ToString("N") + ":";
        var stores = new[] { new RedisSessionStore(connection, TimeProvider.System, prefix), new RedisSessionStore(connection, TimeProvider.System, prefix) };
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(index => stores[index % 2].TryUseNonceAsync("login", "transaction-state", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken)));
        Assert.Single(results, success => success);
    }

    private static SessionData Data(string userId) => new(userId, null, null, userId, [], "access", "refresh", DateTimeOffset.UtcNow.AddMinutes(2))
    { Issuer = "https://idp.test", ProviderSessionId = "provider-session" };
}
