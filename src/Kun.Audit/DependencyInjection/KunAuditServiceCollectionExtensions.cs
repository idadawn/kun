using Kun.Audit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Audit.DependencyInjection;

/// <summary>Service-collection extensions that wire up Kun.Audit abstractions.</summary>
public static class KunAuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="KunAuditOptions"/> and a
    /// <see cref="NullAuditWriter"/> fallback. Call
    /// <c>UseEventBus()</c> on the returned builder, or
    /// <c>.AddKunAuditEntityFrameworkCore()</c> separately, to replace
    /// the fallback with a concrete backend.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional options configurer.</param>
    /// <returns>A <see cref="KunAuditBuilder"/> for further wiring.</returns>
    public static KunAuditBuilder AddKunAudit(
        this IServiceCollection services,
        Action<KunAuditOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<KunAuditOptions>();
        }

        // TryAdd so downstream .UseEventBus() / AddKunAuditEntityFrameworkCore()
        // can replace the fallback with a concrete writer via Replace/AddSingleton.
        services.TryAddSingleton<IAuditWriter, NullAuditWriter>();

        return new KunAuditBuilder(services);
    }

    /// <summary>
    /// Replaces the registered <see cref="IAuditWriter"/> with
    /// <see cref="EventBusAuditWriter"/>, routing audit entries through
    /// the consumer's Outbox as integration events.
    /// </summary>
    /// <remarks>
    /// The consumer is responsible for registering <see cref="AuditLog"/>
    /// in their <c>IEventTypeRegistry</c> under the stable id
    /// <see cref="EventBusAuditWriter.EventTypeId"/> and for providing a
    /// source-generated <c>JsonTypeInfo&lt;AuditLog&gt;</c>.
    /// </remarks>
    public static KunAuditBuilder UseEventBus(this KunAuditBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<IAuditWriter>();
        builder.Services.AddSingleton<IAuditWriter, EventBusAuditWriter>();
        return builder;
    }
}
