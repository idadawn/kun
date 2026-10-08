using Kun.Core.Domain;
using Kun.Core.Security;
using Kun.EntityFrameworkCore.Interceptors;
using Kun.EventBus;
using Kun.EventBus.Background;
using Kun.EventBus.Outbox;
using Kun.MultiTenancy.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IOutboxMessageStore, EfOutboxMessageStore>();

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
    /// 兼容入口：注册 RLS 双拦截器；推荐使用泛型重载自动装配。Consumers must
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
        services.TryAddScoped<TenantRlsConnectionInterceptor>();
        services.TryAddScoped<TenantRlsTransactionInterceptor>();
        return services;
    }

    /// <summary>为指定上下文自动装配 RLS 双拦截器与租户模型缓存键。</summary>
    /// <typeparam name="TContext">声明租户模型缓存键的上下文。</typeparam>
    /// <param name="services">服务容器。</param>
    /// <returns>原服务容器。</returns>
    public static IServiceCollection AddKunTenantRls<TContext>(this IServiceCollection services)
        where TContext : DbContext, IKunMultiTenantDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddKunTenantRls();
        services.ConfigureDbContext<TContext>((sp, options) => options
            .AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>(),
                sp.GetRequiredService<TenantRlsTransactionInterceptor>())
            .ReplaceService<IModelCacheKeyFactory, KunMultiTenantModelCacheKeyFactory>());
        return services;
    }
    /// <summary>为上下文装配审计字段自动填充；模型内配合 ApplyKunDataConventions。</summary>
    public static IServiceCollection AddKunDataConventions<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser>(_ => AnonymousCurrentUser.Instance);
        services.TryAddScoped<AuditFieldsSaveChangesInterceptor>();
        services.ConfigureDbContext<TContext>((sp, options) =>
            options.AddInterceptors(sp.GetRequiredService<AuditFieldsSaveChangesInterceptor>()));
        return services;
    }

}
