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
    /// 默认 EF 存储使用事务行锁；自定义存储须通过 <see cref="UseRedisOutboxLock"/>
    /// 配置共享锁。无锁模式只允许宿主显式注册 <see cref="NullOutboxLockProvider"/>。
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional delegate to configure <see cref="OutboxPublisherOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunOutboxPublisher(
        this IServiceCollection services,
        Action<OutboxPublisherOptions>? configure = null)
    {
        services.AddOptions<OutboxPublisherOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOutboxLockProvider, RequiredOutboxLockProvider>();
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

// EF 默认存储通过事务行锁自行协调。自定义非事务存储必须显式提供共享锁，禁止静默回退无锁。
internal sealed class RequiredOutboxLockProvider : IOutboxLockProvider
{
    public ValueTask<IAsyncDisposable?> TryAcquireAsync(string lockName, TimeSpan ttl, CancellationToken ct = default) =>
        throw new InvalidOperationException("请使用框架 EF Outbox 存储，或配置 UseRedisOutboxLock()；单实例无锁仅允许显式注册 NullOutboxLockProvider。");
}
