using Kun.Core.Domain;
using Kun.EventBus;
using Kun.EventBus.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers Kun EF Core write-side services into the DI container.</summary>
public static class KunEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the unit-of-work, integration-event outbox bridge, and default domain-event
    /// dispatcher. Callers must separately register their <see cref="KunDbContext"/> subclass
    /// via <c>AddDbContext</c>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddKunEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IntegrationEventBuffer>();
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork>();

        // IIntegrationEventBus and IOutboxWriter must resolve to the SAME scoped
        // instance of IntegrationEventOutboxAccumulator so non-aggregate collaborators
        // (IOutboxWriter) enqueue into the same buffer the aggregate flow uses.
        services.TryAddScoped<IntegrationEventOutboxAccumulator>();
        services.TryAddScoped<IIntegrationEventBus>(sp => sp.GetRequiredService<IntegrationEventOutboxAccumulator>());
        services.TryAddScoped<IOutboxWriter>(sp => sp.GetRequiredService<IntegrationEventOutboxAccumulator>());

        services.TryAddScoped<IDomainEventDispatcher, InProcessDomainEventDispatcher>();

        return services;
    }
}
