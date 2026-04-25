using Kun.EventBus.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.EventBus.DependencyInjection;

/// <summary>Registers Kun event bus services.</summary>
public static class KunEventBusServiceCollectionExtensions
{
    /// <summary>Adds event bus abstractions and an in-process outbox buffer.</summary>
    public static IServiceCollection AddKunEventBus(
        this IServiceCollection services,
        Action<KunEventBusBuilder>? configure = null)
    {
        var builder = new KunEventBusBuilder(services);
        configure?.Invoke(builder);

        var registry = new EventTypeRegistry(builder.EventTypeMap, builder.JsonTypeInfoMap);
        services.TryAddSingleton<IEventTypeRegistry>(registry);
        services.TryAddScoped<IntegrationEventBuffer>();
        services.TryAddScoped<IIntegrationEventOutbox>(sp => sp.GetRequiredService<IntegrationEventBuffer>());

        return services;
    }
}
