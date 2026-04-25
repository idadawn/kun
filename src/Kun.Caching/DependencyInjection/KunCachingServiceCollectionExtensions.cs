using Kun.Caching.Abstractions;
using Kun.Caching.Configuration;
using Kun.Caching.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Caching.DependencyInjection;

/// <summary>Service-collection extensions that register <c>Kun.Caching</c> backends.</summary>
public static class KunCachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the cache abstractions and the default JSON serializer.
    /// Pick a backend via <see cref="UseMemory"/> or
    /// <see cref="UseRedis"/> on the returned builder.
    /// </summary>
    public static KunCachingBuilder AddKunCaching(
        this IServiceCollection services,
        Action<KunCachingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<KunCachingOptions>();
        }

        services.TryAddSingleton<IKunCacheSerializer, SystemTextJsonCacheSerializer>();
        return new KunCachingBuilder(services);
    }

    /// <summary>
    /// Registers <see cref="MemoryKunCache"/> as the active
    /// <see cref="IKunCache"/>. Adds the required
    /// <c>IMemoryCache</c> service if the caller has not registered
    /// one already.
    /// </summary>
    public static KunCachingBuilder UseMemory(this KunCachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddMemoryCache();
        builder.Services.RemoveAll<IKunCache>();
        builder.Services.AddSingleton<IKunCache, MemoryKunCache>();
        return builder;
    }

    /// <summary>
    /// Registers <see cref="RedisKunCache"/> as the active
    /// <see cref="IKunCache"/>, wired to a StackExchange Redis
    /// distributed cache built from <paramref name="connectionString"/>.
    /// When <c>KunCachingOptions.FallbackToMemoryOnRedisFailure</c> is
    /// <see langword="true"/>, an in-process <see cref="MemoryKunCache"/>
    /// is registered as the resilience sidecar.
    /// </summary>
    public static KunCachingBuilder UseRedis(
        this KunCachingBuilder builder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        builder.Services.AddStackExchangeRedisCache(opts => opts.Configuration = connectionString);

        // A MemoryKunCache is always eligible as the sidecar; RedisKunCache
        // inspects KunCachingOptions.FallbackToMemoryOnRedisFailure and
        // keeps the reference only when the toggle is on.
        builder.Services.AddMemoryCache();
        builder.Services.TryAddSingleton<MemoryKunCache>();

        builder.Services.RemoveAll<IKunCache>();
        builder.Services.AddSingleton<IKunCache, RedisKunCache>();
        return builder;
    }
}
