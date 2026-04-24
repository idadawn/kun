using System.Diagnostics;
using System.Text.Json;
using Kun.Auditing.DependencyInjection;
using Kun.Auditing.Events;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EventBus.Outbox;

namespace Kun.Auditing;

/// <summary>
/// Default <see cref="IAuditSink"/> implementation that writes
/// <see cref="AuditEventIntegrationEvent"/> directly to the current
/// <see cref="KunDbContext"/>'s Outbox DbSet. The audit row is enrolled in the
/// same EF change tracker batch that the firing <c>SaveChangesAsync</c> is about
/// to persist, so it commits atomically with the business mutation regardless of
/// whether the caller went through <see cref="EfUnitOfWork"/> or invoked
/// <c>DbContext.SaveChangesAsync</c> directly.
/// </summary>
internal sealed class OutboxAuditSink : IAuditSink
{
    private readonly KunDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public OutboxAuditSink(KunDbContext dbContext, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(tenantContext);
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public ValueTask WriteAsync(AuditEventIntegrationEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var payload = JsonSerializer.Serialize(@event, AuditJsonContext.Default.AuditEventIntegrationEvent);
        var activity = Activity.Current;

        var message = new OutboxMessage(
            id: Guid.NewGuid(),
            eventTypeId: AuditingServiceCollectionExtensions.AuditEventStableId,
            payload: payload,
            tenantId: _tenantContext.TenantId,
            traceParent: activity?.Id,
            traceState: activity?.TraceStateString,
            occurredAt: DateTimeOffset.UtcNow);

        _dbContext.OutboxMessages.Add(message);
        return ValueTask.CompletedTask;
    }
}
