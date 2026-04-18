using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Kun.EventBus.RabbitMQ.Consuming;
using Kun.EventBus.RabbitMQ.Options;
using Kun.EventBus.RabbitMQ.Publishing;

namespace Kun.EventBus.RabbitMQ.DependencyInjection;

/// <summary>Extension methods for registering the RabbitMQ broker adapter.</summary>
public static class KunRabbitMqServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RabbitMQ transport for <see cref="IIntegrationEventPublisher"/> and
    /// starts the consumer background service for any subscriptions configured via <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Configures <see cref="RabbitMqOptions"/>.</param>
    /// <param name="configure">Optional builder for registering event subscriptions.</param>
    public static IServiceCollection AddRabbitMq(
        this IServiceCollection services,
        Action<RabbitMqOptions> configureOptions,
        Action<KunRabbitMqBuilder>? configure = null)
    {
        services.Configure(configureOptions);

        // Connection provider — singleton owning the AMQP connection.
        services.TryAddSingleton<RabbitMqConnectionProvider>();

        // Publisher — singleton, thread-safe (creates a channel per publish).
        services.TryAddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();

        // Builder for subscriptions.
        var builder = new KunRabbitMqBuilder(services);
        configure?.Invoke(builder);

        // Subscriber background service.
        services.AddSingleton<RabbitMqIntegrationEventSubscriber>(sp =>
            new RabbitMqIntegrationEventSubscriber(
                sp.GetRequiredService<RabbitMqConnectionProvider>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IEventTypeRegistry>(),
                sp.GetRequiredService<IOptions<RabbitMqOptions>>(),
                sp.GetServices<SubscriptionRegistration>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RabbitMqIntegrationEventSubscriber>>()));

        services.AddSingleton<IHostedService>(sp =>
            sp.GetRequiredService<RabbitMqIntegrationEventSubscriber>());

        return services;
    }
}
