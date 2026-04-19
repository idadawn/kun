using Kun.EventBus.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Kun.EventBus.DependencyInjection;

/// <summary>Extension methods to register the outbox publisher background service.</summary>
public static class KunOutboxPublisherExtensions
{
    /// <summary>
    /// Registers <see cref="OutboxPublisherBackgroundService"/> and its dependencies.
    /// The default <see cref="IOutboxLockProvider"/> is <see cref="NullOutboxLockProvider"/>,
    /// which is safe for single-instance deployments. Call <see cref="UseRedisOutboxLock"/>
    /// afterwards to enable distributed locking for multi-pod deployments.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional delegate to configure <see cref="OutboxPublisherOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunOutboxPublisher(
        this IServiceCollection services,
        Action<OutboxPublisherOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOutboxLockProvider>(NullOutboxLockProvider.Instance);
        services.AddHostedService<OutboxPublisherBackgroundService>();
        return services;
    }

    /// <summary>
    /// Replaces the default <see cref="NullOutboxLockProvider"/> with
    /// <see cref="RedisOutboxLockProvider"/> for distributed-lock semantics across multiple pods.
    /// Requires <see cref="IConnectionMultiplexer"/> to be registered in the container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection UseRedisOutboxLock(this IServiceCollection services)
    {
        // Remove the null provider (if registered) and replace with Redis.
        var descriptor = ServiceDescriptor.Singleton<IOutboxLockProvider, RedisOutboxLockProvider>();
        services.Replace(descriptor);
        return services;
    }
}
