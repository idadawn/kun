using Kun.EventBus.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.EventBus.DependencyInjection;

/// <summary>Extension methods to register the outbox publisher background service.</summary>
public static class KunOutboxPublisherExtensions
{
    /// <summary>
    /// Registers <see cref="OutboxPublisherBackgroundService"/> and its dependencies.
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
        services.AddHostedService<OutboxPublisherBackgroundService>();
        return services;
    }
}
