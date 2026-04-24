using Kun.Audit.Behaviors;
using Kun.Core.Cqrs;
using Kun.Mediator;
using Kun.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Audit.DependencyInjection;

/// <summary>
/// DI entry points for Kun.Audit. Callers register defaults for the chain state
/// and sink via <see cref="AddKunAudit"/> and opt each auditable command into
/// the pipeline via <see cref="AddAuditBehavior{TRequest, TResponse}"/>.
/// </summary>
public static class KunAuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory <see cref="IAuditChainState"/> and
    /// <see cref="IAuditSink"/> defaults, plus <see cref="TimeProvider.System"/>.
    /// Uses <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton"/> throughout
    /// so callers can override any of them — a production deployment will typically
    /// replace the sink with a durable implementation and the chain state with a
    /// DB-backed coordinator.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddKunAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAuditChainState, InMemoryAuditChainState>();
        services.TryAddSingleton<IAuditSink, InMemoryAuditSink>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
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
