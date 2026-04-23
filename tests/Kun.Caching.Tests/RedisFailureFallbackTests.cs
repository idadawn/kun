using FluentAssertions;
using Kun.Caching.Configuration;
using Kun.Caching.Implementations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Kun.Caching.Tests;

/// <summary>
/// Verifies the <c>FallbackToMemoryOnRedisFailure</c> toggle: with the flag on,
/// Redis <c>RedisConnectionException</c> is swallowed and the operation is
/// routed to the sidecar <see cref="MemoryKunCache"/>; with the flag off, the
/// exception propagates unchanged.
/// </summary>
public sealed class RedisFailureFallbackTests
{
    private static MemoryKunCache BuildMemoryFallback()
        => new(new MemoryCache(new MemoryCacheOptions()), Options.Create(new KunCachingOptions()));

    private static IDistributedCache BuildFailingRedis()
    {
        var distributed = Substitute.For<IDistributedCache>();
        distributed.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<byte[]?>>(_ => Task.FromException<byte[]?>(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test: redis unreachable")));
        distributed.When(d => d.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test: redis unreachable"));
        distributed.When(d => d.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test: redis unreachable"));
        return distributed;
    }

    [Fact]
    public async Task FallbackEnabled_SwallowsRedisExceptionAndUsesMemoryRoundTrip()
    {
        var memory = BuildMemoryFallback();
        var options = Options.Create(new KunCachingOptions { FallbackToMemoryOnRedisFailure = true });
        var redis = new RedisKunCache(
            BuildFailingRedis(),
            new SystemTextJsonCacheSerializer(),
            options,
            NullLogger<RedisKunCache>.Instance,
            memory);
        var ct = TestContext.Current.CancellationToken;

        await redis.SetAsync("fallback-key", "fallback-value", cancellationToken: ct);
        var hit = await redis.GetAsync<string>("fallback-key", ct);

        hit.Should().Be("fallback-value");
    }

    [Fact]
    public async Task FallbackDisabled_PropagatesRedisException()
    {
        var options = Options.Create(new KunCachingOptions { FallbackToMemoryOnRedisFailure = false });
        var redis = new RedisKunCache(
            BuildFailingRedis(),
            new SystemTextJsonCacheSerializer(),
            options,
            NullLogger<RedisKunCache>.Instance,
            BuildMemoryFallback());
        var ct = TestContext.Current.CancellationToken;

        var getAct = async () => await redis.GetAsync<string>("any", ct);
        var setAct = async () => await redis.SetAsync("any", "v", cancellationToken: ct);
        var removeAct = async () => await redis.RemoveAsync("any", ct);

        await getAct.Should().ThrowAsync<RedisConnectionException>();
        await setAct.Should().ThrowAsync<RedisConnectionException>();
        await removeAct.Should().ThrowAsync<RedisConnectionException>();
    }
}
