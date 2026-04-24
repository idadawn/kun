using Kun.Audit.Abstractions;
using Kun.Audit.Behaviors;
using Kun.Core.Cqrs;
using Kun.Mediator;
using Kun.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Audit.DependencyInjection;

/// <summary>
/// Service-collection extensions for Kun.Audit. Wires up both halves of the module:
/// the writer/reader abstraction (<see cref="IAuditWriter"/> / <see cref="IAuditReader"/>)
/// and the tamper-evident hash-chain primitives (<see cref="IAuditChainState"/> /
/// <see cref="IAuditSink"/> / <see cref="AuditLogPipelineBehavior{TRequest,TResponse}"/>).
/// </summary>
public static class KunAuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Kun.Audit defaults: <see cref="KunAuditOptions"/>, a
    /// <see cref="NullAuditWriter"/> writer fallback, plus the in-memory hash-chain
    /// state/sink and a <see cref="TimeProvider"/>. All registrations use
    /// <c>TryAdd</c> so downstream callers can override (e.g. <c>UseEventBus()</c>
    /// replaces the writer; production deployments replace the in-memory chain
    /// state/sink with durable implementations).
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

        // Writer/reader abstraction: fallback writer that drops entries. Replace via
        // UseEventBus() or AddKunAuditEntityFrameworkCore() for a real backend.
        services.TryAddSingleton<IAuditWriter, NullAuditWriter>();

        // Hash-chain primitives: in-memory defaults for tests and single-instance dev.
        // Production deployments should register durable implementations before calling
        // AddKunAudit, or replace these registrations afterwards.
        services.TryAddSingleton<IAuditChainState, InMemoryAuditChainState>();
        services.TryAddSingleton<IAuditSink, InMemoryAuditSink>();
        services.TryAddSingleton(TimeProvider.System);

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

    /// <summary>
    /// Registers <see cref="AuditLogPipelineBehavior{TRequest, TResponse}"/> for the
    /// given request/response pair on a <see cref="KunMediatorBuilder"/>. Call once
    /// per command type that should be audited; queries left unregistered will not
    /// generate audit noise.
    /// </summary>
    public static KunMediatorBuilder AddAuditBehavior<TRequest, TResponse>(
        this KunMediatorBuilder builder)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddPipelineBehavior<TRequest, TResponse, AuditLogPipelineBehavior<TRequest, TResponse>>();
        return builder;
    }
}
