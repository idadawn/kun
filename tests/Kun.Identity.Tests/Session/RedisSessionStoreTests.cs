using System.Collections.Concurrent;
using FluentAssertions;
using Kun.Caching.Abstractions;
using Kun.Identity.Abstractions.Session;
using Kun.Identity.Implementations.Session;
using Xunit;

namespace Kun.Identity.Tests.Session;

public sealed class RedisSessionStoreTests
{
    private static SessionData Sample() => new(
        UserId: "user-2",
        TenantId: "tenant-b",
        Email: null,
        Name: "Bob",
        Roles: new[] { "viewer" },
        AccessToken: "AT",
        RefreshToken: null,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task SetThenGet_UsesKunCacheKeyPrefix()
    {
        var cache = new BytesCacheFake();
        var store = new RedisSessionStore(cache);
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("sid-1", Sample(), TimeSpan.FromMinutes(5), ct);

        cache.Store.Keys.Should().ContainSingle().Which.Should().Be("kun:session:sid-1");
    }

    [Fact]
    public async Task Get_ReturnsNullOnMiss()
    {
        var store = new RedisSessionStore(new BytesCacheFake());
        var ct = TestContext.Current.CancellationToken;

        (await store.GetAsync("sid-missing", ct)).Should().BeNull();
    }

    [Fact]
    public async Task RoundTrip_PreservesAllSessionFields()
    {
        var store = new RedisSessionStore(new BytesCacheFake());
        var ct = TestContext.Current.CancellationToken;
        var data = Sample();

        await store.SetAsync("sid", data, TimeSpan.FromMinutes(5), ct);
        var hit = await store.GetAsync("sid", ct);

        hit.Should().NotBeNull();
        hit!.UserId.Should().Be(data.UserId);
        hit.TenantId.Should().Be(data.TenantId);
        hit.Name.Should().Be(data.Name);
        hit.Roles.Should().Equal(data.Roles);
        hit.AccessToken.Should().Be(data.AccessToken);
    }

    [Fact]
    public async Task Set_PassesTtlToCache()
    {
        var cache = new BytesCacheFake();
        var store = new RedisSessionStore(cache);
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("sid", Sample(), TimeSpan.FromMinutes(15), ct);

        cache.LastTtl.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Delete_RemovesEntry()
    {
        var cache = new BytesCacheFake();
        var store = new RedisSessionStore(cache);
        var ct = TestContext.Current.CancellationToken;

        await store.SetAsync("sid", Sample(), TimeSpan.FromMinutes(5), ct);
        await store.DeleteAsync("sid", ct);

        cache.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Set_RejectsNonPositiveTtl()
    {
        var store = new RedisSessionStore(new BytesCacheFake());
        var ct = TestContext.Current.CancellationToken;

        var act = () => store.SetAsync("sid", Sample(), TimeSpan.Zero, ct);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Minimal <see cref="IKunCache"/> double that records writes to an
    /// in-memory dictionary — enough to verify <c>RedisSessionStore</c>
    /// routes calls through the byte[] surface rather than the generic
    /// (AOT-hostile) overloads.
    /// </summary>
    private sealed class BytesCacheFake : IKunCache
    {
        public ConcurrentDictionary<string, byte[]> Store { get; } = new(StringComparer.Ordinal);
        public TimeSpan? LastTtl { get; private set; }

        public Task<byte[]?> GetBytesAsync(string key, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(Store.TryGetValue(key, out var v) ? v : null);

        public Task SetBytesAsync(string key, byte[] value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            Store[key] = value;
            LastTtl = ttl;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            Store.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => throw new NotImplementedException("Generic path must not be used by RedisSessionStore.");

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
            => throw new NotImplementedException("Generic path must not be used by RedisSessionStore.");

        public Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
