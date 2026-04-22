using Kun.Auditing.Events;
using Kun.Auditing.Middleware;
using Kun.EventBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Auditing.DependencyInjection;

/// <summary>
/// DI extension methods for wiring the Kun.Auditing module.
/// </summary>
/// <remarks>
/// <para>
/// <b>Explicit registration (plan A3 path c)</b>. Auditable entity types must be declared
/// up front via <see cref="AddAuditable{TEntity}(IServiceCollection)"/>; the
/// <see cref="AuditableAttribute"/> marker on the entity class is not sufficient — it is
/// docs-only and never scanned at runtime. This design eliminates reflection and keeps
/// the module AOT-publish-clean.
/// </para>
/// </remarks>
public static class AuditingServiceCollectionExtensions
{
    /// <summary>
    /// Registers audit infrastructure into <paramref name="services"/>: the registry, the
    /// default outbox-backed <see cref="IAuditSink"/>, the EF interceptor, the request-scoped
    /// <see cref="AuditContext"/>, and the <see cref="AuditContextMiddleware"/>.
    /// </summary>
    /// <remarks>
    /// Consumers must also:
    /// <list type="bullet">
    ///   <item>Call <c>AddAuditable&lt;TEntity&gt;()</c> for each auditable entity type.</item>
    ///   <item>Register the stable event id with <c>AddKunEventBus(b =&gt; b.RegisterAuditing())</c>.</item>
    ///   <item>Attach the interceptor to the relevant DbContext via
    ///   <c>options.AddInterceptors(sp.GetRequiredService&lt;AuditingSaveChangesInterceptor&gt;())</c>
    ///   or use <see cref="AddAuditingInterceptor"/> on the <see cref="DbContextOptionsBuilder"/>.</item>
    ///   <item>Mount <see cref="AuditContextMiddleware"/> into the ASP.NET Core pipeline.</item>
    /// </list>
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddKunAuditing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AuditableEntityTypeCollection>();
        services.TryAddSingleton<IAuditableEntityRegistry>(sp =>
            new AuditableEntityRegistry(sp.GetRequiredService<AuditableEntityTypeCollection>().Types));

        services.TryAddScoped<AuditContext>();
        services.TryAddScoped<IAuditSink, OutboxAuditSink>();
        services.TryAddScoped<AuditingSaveChangesInterceptor>();
        services.TryAddScoped<AuditContextMiddleware>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TEntity"/> as an auditable type so that mutations of
    /// it tracked by any <see cref="DbContext"/> will emit
    /// <see cref="AuditEventIntegrationEvent"/> on <c>SaveChangesAsync</c>. Safe to call
    /// repeatedly for the same type.
    /// </summary>
    /// <typeparam name="TEntity">Entity class to audit.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAuditable<TEntity>(this IServiceCollection services)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AuditableEntityTypeCollection>();

        var descriptor = services
            .FirstOrDefault(d => d.ServiceType == typeof(AuditableEntityTypeCollection))
            ?? throw new InvalidOperationException(
                "AuditableEntityTypeCollection was not registered. Call AddKunAuditing() first, or AddAuditable<T>() will register it transiently.");

        // Mutate the seed bag via the descriptor's implementation instance when present;
        // otherwise hand the job off to a startup resolution by replacing the descriptor
        // with a factory that closes over the type list.
        if (descriptor.ImplementationInstance is AuditableEntityTypeCollection existing)
        {
            existing.Add(typeof(TEntity));
            return services;
        }

        // Replace the registration with an eagerly-constructed instance so subsequent calls
        // can mutate it directly, preserving call-order accumulation.
        var seed = new AuditableEntityTypeCollection();
        seed.Add(typeof(TEntity));
        services.Remove(descriptor);
        services.AddSingleton(seed);
        return services;
    }

    /// <summary>
    /// Convenience method to attach the <see cref="AuditingSaveChangesInterceptor"/> to a
    /// <see cref="DbContextOptionsBuilder"/> via DI resolution.
    /// </summary>
    /// <remarks>
    /// Callers use this inside their <c>AddDbContext&lt;T&gt;((sp, options) =&gt; ...)</c>
    /// factory, e.g.
    /// <code>
    /// services.AddDbContext&lt;CatalogDbContext&gt;((sp, options) =&gt;
    ///     options.UseNpgsql(cs)
    ///            .UseKunSchema("catalog")
    ///            .AddAuditingInterceptor(sp));
    /// </code>
    /// </remarks>
    /// <param name="builder">The options builder.</param>
    /// <param name="serviceProvider">Service provider from the <c>AddDbContext</c> factory.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static DbContextOptionsBuilder AddAuditingInterceptor(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return builder.AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>());
    }

    /// <summary>
    /// Registers <see cref="AuditEventIntegrationEvent"/> with the Kun event-type registry
    /// under the stable id <c>kun.audit.event.v1</c>. Call from inside
    /// <c>services.AddKunEventBus(b =&gt; b.RegisterAuditing())</c> so the outbox publisher
    /// emits the correct <c>EventTypeId</c> for audit messages.
    /// </summary>
    /// <param name="registrar">The event-type registrar.</param>
    /// <returns>The same <paramref name="registrar"/> for chaining.</returns>
    public static IEventTypeRegistrar RegisterAuditing(this IEventTypeRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        return registrar.Register<AuditEventIntegrationEvent>(AuditEventStableId);
    }

    /// <summary>Stable Outbox event id for <see cref="AuditEventIntegrationEvent"/>.</summary>
    public const string AuditEventStableId = "kun.audit.event.v1";
}
