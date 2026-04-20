using System.Diagnostics;
using System.Text.Json;
using Kun.Core.MultiTenancy;
using Kun.EventBus;
using Kun.EventBus.Outbox;

namespace Kun.EntityFrameworkCore;

/// <summary>
/// <see cref="IIntegrationEventBus"/> implementation that serializes integration events and
/// appends them to the <see cref="IntegrationEventBuffer"/> with current trace and tenant context.
/// The buffer is flushed atomically by <see cref="EfUnitOfWork"/> during <c>SaveChangesAsync</c>.
/// </summary>
public sealed class IntegrationEventOutboxAccumulator : IIntegrationEventBus, IOutboxWriter
{
    private readonly IntegrationEventBuffer _buffer;
    private readonly IEventTypeRegistry _registry;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new <see cref="IntegrationEventOutboxAccumulator"/>.</summary>
    public IntegrationEventOutboxAccumulator(
        IntegrationEventBuffer buffer,
        IEventTypeRegistry registry,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(tenantContext);
        _buffer = buffer;
        _registry = registry;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc/>
    public void Publish<TEvent>(TEvent @event) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventTypeId = _registry.GetEventType(typeof(TEvent))
            ?? throw new InvalidOperationException(
                $"Event type '{typeof(TEvent).FullName}' is not registered in IEventTypeRegistry. " +
                "Register it via IEventTypeRegistrar before publishing.");

        var payload = JsonSerializer.Serialize(@event);
        var activity = Activity.Current;

        var message = new OutboxMessage(
            id: Guid.NewGuid(),
            eventTypeId: eventTypeId,
            payload: payload,
            tenantId: _tenantContext.TenantId,
            traceParent: activity?.Id,
            traceState: activity?.TraceStateString,
            occurredAt: DateTimeOffset.UtcNow);

        _buffer.Add(message);
    }

    /// <inheritdoc />
    public void Enqueue<TEvent>(TEvent @event) where TEvent : class => Publish(@event);
}
