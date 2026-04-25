using Kun.Core.Domain;
using Kun.EntityFrameworkCore.Interceptors;
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
    /// <remarks>
    /// Also registers <see cref="TenantRlsConnectionInterceptor"/> as Scoped; it does not
    /// activate unless the caller adds it to their <c>DbContextOptionsBuilder</c> via
    /// <c>opts.AddInterceptors(sp.GetRequiredService&lt;TenantRlsConnectionInterceptor&gt;())</c>.
    /// </remarks>
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

        // Opt-in defence-in-depth: resolves to a no-op unless the caller wires it into
        // their DbContextOptionsBuilder. See TenantRlsConnectionInterceptor docs.
        services.TryAddScoped<TenantRlsConnectionInterceptor>();

        return services;
    }

    /// <summary>
    /// Opt-in registration for <see cref="TenantRlsTransactionInterceptor"/>. Consumers must
    /// also wire the interceptor into their DbContext options via DI resolution, e.g.
    /// <code>
    /// services.AddDbContext&lt;CatalogDbContext&gt;((sp, options) =&gt;
    ///     options.UseNpgsql(cs)
    ///            .UseKunSchema("catalog")
    ///            .AddInterceptors(sp.GetRequiredService&lt;TenantRlsTransactionInterceptor&gt;()));
    /// </code>
    /// Scoped lifetime — each request's <see cref="Kun.Core.MultiTenancy.ITenantContext"/>
    /// is resolved per transaction. Not added automatically because the interceptor fires a
    /// <c>set_config</c> round-trip on every transaction; services without RLS should not pay
    /// that cost.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddKunTenantRls(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<TenantRlsTransactionInterceptor>();
        return services;
    }
}
