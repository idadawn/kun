using Kun.Auditing.Events;

namespace Kun.Auditing;

/// <summary>
/// Terminal destination for <see cref="AuditEventIntegrationEvent"/> records produced by
/// <see cref="AuditingSaveChangesInterceptor"/>. The default implementation forwards to
/// <see cref="Kun.EventBus.Outbox.IOutboxWriter"/> so audit events flow atomically with
/// the current <c>SaveChangesAsync</c> transaction.
/// </summary>
/// <remarks>
/// Custom sinks (e.g. a direct-write ClickHouse adapter) must preserve the atomicity
/// contract: anything that can fail outside the ambient transaction defeats the audit
/// reliability guarantee. Prefer layering a decorator over the outbox sink instead of
/// replacing it.
/// </remarks>
public interface IAuditSink
{
    /// <summary>
    /// Enqueues <paramref name="event"/> for durable delivery. Implementations must be
    /// safe to call from within EF Core's <c>SavingChangesAsync</c> interceptor phase —
    /// no I/O, no blocking waits.
    /// </summary>
    /// <param name="event">The audit event to persist.</param>
    /// <param name="cancellationToken">Cancellation token forwarded from EF Core.</param>
    ValueTask WriteAsync(AuditEventIntegrationEvent @event, CancellationToken cancellationToken);
}
