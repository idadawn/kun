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
        services.TryAddScoped<IIntegrationEventBus, IntegrationEventOutboxAccumulator>();
        services.TryAddScoped<IDomainEventDispatcher, InProcessDomainEventDispatcher>();

        return services;
    }
}
