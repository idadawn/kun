using System.Diagnostics;
using System.Text.Json;
using Kun.Auditing.DependencyInjection;
using Kun.Auditing.Events;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EventBus.Outbox;
using Microsoft.Extensions.DependencyInjection;

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
/// <remarks>
/// <para>
/// <b>Why <see cref="IServiceProvider"/> instead of injecting <see cref="KunDbContext"/>:</b>
/// the sink is consumed from <see cref="AuditingSaveChangesInterceptor"/>, which itself is
/// resolved by EF Core's <c>AddDbContext</c> factory while the <see cref="KunDbContext"/>
/// is still under construction. Constructor-injecting <see cref="KunDbContext"/> here would
/// re-enter that factory and either throw a circular-dependency exception or — worse — hang
/// silently in some scoped-resolution paths. Resolving <see cref="KunDbContext"/> at
/// <see cref="WriteAsync"/> time defers the lookup until after construction has completed.
/// </para>
/// </remarks>
internal sealed class OutboxAuditSink : IAuditSink
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITenantContext _tenantContext;

    public OutboxAuditSink(IServiceProvider serviceProvider, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(tenantContext);
        _serviceProvider = serviceProvider;
        _tenantContext = tenantContext;
    }

    public ValueTask WriteAsync(AuditEventIntegrationEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var dbContext = _serviceProvider.GetRequiredService<KunDbContext>();
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

        dbContext.OutboxMessages.Add(message);
        return ValueTask.CompletedTask;
    }
}
