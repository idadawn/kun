using Kun.Auditing.Events;
using Kun.EventBus.Outbox;

namespace Kun.Auditing;

/// <summary>
/// Default <see cref="IAuditSink"/> implementation that forwards
/// <see cref="AuditEventIntegrationEvent"/> records to
/// <see cref="IOutboxWriter"/>. The outbox buffer is flushed atomically by
/// <c>EfUnitOfWork.SaveChangesAsync</c>, preserving the "same transaction as business
/// mutation" audit-reliability invariant.
/// </summary>
internal sealed class OutboxAuditSink : IAuditSink
{
    private readonly IOutboxWriter _outbox;

    public OutboxAuditSink(IOutboxWriter outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        _outbox = outbox;
    }

    public ValueTask WriteAsync(AuditEventIntegrationEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _outbox.Enqueue(@event);
        return ValueTask.CompletedTask;
    }
}
