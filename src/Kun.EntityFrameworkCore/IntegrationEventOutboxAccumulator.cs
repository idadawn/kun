using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    [RequiresUnreferencedCode(
        "Falls back to reflection-based JSON serialization when the event type was registered " +
        "without a source-generated JsonTypeInfo. AOT consumers must use " +
        "IEventTypeRegistrar.Register<TEvent>(string, JsonTypeInfo<TEvent>) at registration time.")]
    [RequiresDynamicCode(
        "Falls back to reflection-based JSON serialization when the event type was registered " +
        "without a source-generated JsonTypeInfo.")]
    public void Publish<TEvent>(TEvent @event) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventTypeId = _registry.GetEventType(typeof(TEvent))
            ?? throw new InvalidOperationException(
                $"Event type '{typeof(TEvent).FullName}' is not registered in IEventTypeRegistry. " +
                "Register it via IEventTypeRegistrar before publishing.");

        // Prefer source-generated JsonTypeInfo when the consumer registered it (AOT-safe).
        // Fall back to reflection-based Serialize for legacy registrations; this path is
        // explicitly annotated unsafe for trim/AOT above so the analyzer warns at callers
        // rather than silently breaking at publish time.
        string payload;
        var typeInfo = _registry.GetJsonTypeInfo(typeof(TEvent));
        if (typeInfo is System.Text.Json.Serialization.Metadata.JsonTypeInfo<TEvent> typed)
        {
            payload = JsonSerializer.Serialize(@event, typed);
        }
        else
        {
            payload = JsonSerializer.Serialize(@event);
        }

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
    [RequiresUnreferencedCode(
        "Delegates to Publish<TEvent>; see Publish for AOT-safety requirements.")]
    [RequiresDynamicCode(
        "Delegates to Publish<TEvent>; see Publish for AOT-safety requirements.")]
    public void Enqueue<TEvent>(TEvent @event) where TEvent : class => Publish(@event);
}
