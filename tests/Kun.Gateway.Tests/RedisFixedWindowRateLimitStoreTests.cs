using Kun.Gateway.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Kun.Gateway.Tests;

public sealed class RedisFixedWindowRateLimitStoreTests
{
    [Fact]
    public void Ctor_NullRedis_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisFixedWindowRateLimitStore(null!, Options.Create(new RedisRateLimitStoreOptions())));
    }

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        Assert.Throws<ArgumentNullException>(() =>
            new RedisFixedWindowRateLimitStore(redis, null!));
    }

    [Fact]
    public async Task HitAsync_EmptyPartitionKey_Throws()
    {
        var store = BuildStore();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.HitAsync(string.Empty, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HitAsync_WhitespacePartitionKey_Throws()
    {
        var store = BuildStore();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.HitAsync("   ", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HitAsync_ZeroWindow_Throws()
    {
        var store = BuildStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.HitAsync("tenant-a", TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HitAsync_NegativeWindow_Throws()
    {
        var store = BuildStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.HitAsync("tenant-a", TimeSpan.FromSeconds(-1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HitAsync_FirstHit_SetsTtlAndReturnsCount1()
    {
        var db = Substitute.For<IDatabase>();
        db.StringIncrementAsync(Arg.Any<RedisKey>(), 1).Returns(1L);
        db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan>()).Returns(true);

        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase().Returns(db);

        var store = new RedisFixedWindowRateLimitStore(redis, Options.Create(new RedisRateLimitStoreOptions()));
        var result = await store.HitAsync("tenant-a", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Count);
        Assert.Equal(TimeSpan.FromMinutes(1), result.ResetsIn);
        await db.Received(1).KeyExpireAsync(Arg.Any<RedisKey>(), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task HitAsync_SubsequentHit_DoesNotResetTtlAndReturnsCurrentTtl()
    {
        var db = Substitute.For<IDatabase>();
        db.StringIncrementAsync(Arg.Any<RedisKey>(), 1).Returns(7L);
        db.KeyTimeToLiveAsync(Arg.Any<RedisKey>()).Returns(TimeSpan.FromSeconds(23));

        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase().Returns(db);

        var store = new RedisFixedWindowRateLimitStore(redis, Options.Create(new RedisRateLimitStoreOptions()));
        var result = await store.HitAsync("tenant-a", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Assert.Equal(7, result.Count);
        Assert.Equal(TimeSpan.FromSeconds(23), result.ResetsIn);
        await db.DidNotReceive().KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task HitAsync_KeyPrefixApplied()
    {
        var db = Substitute.For<IDatabase>();
        db.StringIncrementAsync(Arg.Any<RedisKey>(), 1).Returns(1L);
        db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan>()).Returns(true);

        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase().Returns(db);

        var opts = new RedisRateLimitStoreOptions { KeyPrefix = "kun:staging:rl" };
        var store = new RedisFixedWindowRateLimitStore(redis, Options.Create(opts));

        await store.HitAsync("tenant-a", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await db.Received(1).StringIncrementAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "kun:staging:rl:tenant-a"), 1);
    }

    [Fact]
    public void AddKunRedisRateLimitStore_RegistersStoreAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());

        services.AddKunRedisRateLimitStore();

        using var sp = services.BuildServiceProvider();
        var a = sp.GetRequiredService<IRedisRateLimitStore>();
        var b = sp.GetRequiredService<IRedisRateLimitStore>();
        Assert.Same(a, b);
        Assert.IsType<RedisFixedWindowRateLimitStore>(a);
    }

    [Fact]
    public void AddKunRedisRateLimitStore_ConfigureAppliesKeyPrefix()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());

        services.AddKunRedisRateLimitStore(o => o.KeyPrefix = "kun:prod:rl");

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<RedisRateLimitStoreOptions>>().Value;
        Assert.Equal("kun:prod:rl", opts.KeyPrefix);
    }

    private static RedisFixedWindowRateLimitStore BuildStore()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        return new RedisFixedWindowRateLimitStore(redis, Options.Create(new RedisRateLimitStoreOptions()));
    }
}
