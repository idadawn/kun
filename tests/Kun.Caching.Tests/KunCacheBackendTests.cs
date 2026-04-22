using FluentAssertions;
using Kun.Caching.Abstractions;
using Kun.Caching.Configuration;
using Kun.Caching.Implementations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit;

namespace Kun.Caching.Tests;

/// <summary>
/// Behavioural contract tests that run against both <see cref="MemoryKunCache"/>
/// and <see cref="RedisKunCache"/>. Each test instantiates the cache via
/// the <c>BackendFactory</c> theory data so a single failing assertion flags
/// divergence between the two backends.
/// </summary>
public sealed class KunCacheBackendTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _redis;

    public KunCacheBackendTests(RedisFixture redis) => _redis = redis;

    public static TheoryData<string> BackendNames => new() { "memory", "redis" };

    private IKunCache CreateCache(string backend, KunCachingOptions? options = null)
    {
        options ??= new KunCachingOptions();
        var optionsAccessor = Options.Create(options);
        var serializer = new SystemTextJsonCacheSerializer();

        return backend switch
        {
            "memory" => new MemoryKunCache(
                new MemoryCache(new MemoryCacheOptions()),
                optionsAccessor),
            "redis" => new RedisKunCache(
                new RedisCache(Options.Create(new RedisCacheOptions { Configuration = _redis.ConnectionString })),
                serializer,
                optionsAccessor,
                NullLogger<RedisKunCache>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public async Task Set_ThenGet_RoundTripsValue(string backend)
    {
        var cache = CreateCache(backend, new KunCachingOptions { KeyPrefix = $"rt-{Guid.NewGuid():N}-" });
        var ct = TestContext.Current.CancellationToken;

        await cache.SetAsync("user:42", new CachedUser(42, "alice"), cancellationToken: ct);
        var hit = await cache.GetAsync<CachedUser>("user:42", ct);

        hit.Should().NotBeNull();
        hit!.Id.Should().Be(42);
        hit.Name.Should().Be("alice");
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public async Task Get_ReturnsNullOnMiss(string backend)
    {
        var cache = CreateCache(backend, new KunCachingOptions { KeyPrefix = $"miss-{Guid.NewGuid():N}-" });
        var ct = TestContext.Current.CancellationToken;

        var value = await cache.GetAsync<CachedUser>("absent", ct);

        value.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public async Task Remove_EvictsValue(string backend)
    {
        var cache = CreateCache(backend, new KunCachingOptions { KeyPrefix = $"rm-{Guid.NewGuid():N}-" });
        var ct = TestContext.Current.CancellationToken;

        await cache.SetAsync("transient", new CachedUser(1, "tmp"), cancellationToken: ct);
        await cache.RemoveAsync("transient", ct);

        (await cache.GetAsync<CachedUser>("transient", ct)).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public async Task Ttl_ExpiresEntry(string backend)
    {
        var cache = CreateCache(backend, new KunCachingOptions { KeyPrefix = $"ttl-{Guid.NewGuid():N}-" });
        var ct = TestContext.Current.CancellationToken;

        await cache.SetAsync("soon-gone", new CachedUser(7, "fleeting"), TimeSpan.FromMilliseconds(200), ct);
        await Task.Delay(600, ct);

        (await cache.GetAsync<CachedUser>("soon-gone", ct)).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public async Task GetOrSet_SingleFlight_RunsFactoryExactlyOnce(string backend)
    {
        var cache = CreateCache(backend, new KunCachingOptions { KeyPrefix = $"sf-{Guid.NewGuid():N}-" });
        var ct = TestContext.Current.CancellationToken;
        var factoryCalls = 0;
        var gate = new TaskCompletionSource();

        async Task<CachedUser> Factory(CancellationToken t)
        {
            Interlocked.Increment(ref factoryCalls);
            await gate.Task.WaitAsync(t);
            return new CachedUser(99, "solo");
        }

        var tasks = Enumerable.Range(0, 24)
            .Select(_ => Task.Run(() => cache.GetOrSetAsync("hot-key", Factory, cancellationToken: ct), ct))
            .ToArray();

        await Task.Delay(100, ct);
        gate.SetResult();

        var results = await Task.WhenAll(tasks);

        factoryCalls.Should().Be(1);
        results.Should().OnlyContain(r => r.Id == 99 && r.Name == "solo");
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public async Task KeyPrefix_IsolatesBetweenCaches(string backend)
    {
        var a = CreateCache(backend, new KunCachingOptions { KeyPrefix = $"a-{Guid.NewGuid():N}-" });
        var b = CreateCache(backend, new KunCachingOptions { KeyPrefix = $"b-{Guid.NewGuid():N}-" });
        var ct = TestContext.Current.CancellationToken;

        await a.SetAsync("shared", new CachedUser(1, "alpha"), cancellationToken: ct);

        (await b.GetAsync<CachedUser>("shared", ct)).Should().BeNull();
    }

    private sealed record CachedUser(int Id, string Name);
}

/// <summary>xUnit shared fixture that spins up a throwaway Redis container once per test class.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());
    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
