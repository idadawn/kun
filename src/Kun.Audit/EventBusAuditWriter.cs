using Kun.Audit.Abstractions;
using Kun.EventBus;

namespace Kun.Audit;

/// <summary>
/// <see cref="IAuditWriter"/> that fans audit entries out as
/// <c>kun.audit.record.v1</c> integration events via
/// <see cref="IIntegrationEventBus"/>, riding the consumer's Outbox for
/// atomicity with the business transaction.
/// </summary>
/// <remarks>
/// <para>
/// Consumers must register <see cref="AuditLog"/> in their event type
/// registry under the stable id <c>kun.audit.record.v1</c> and register
/// the corresponding <c>JsonTypeInfo</c> in their
/// <c>JsonSerializerContext</c> — Kun does not own the consumer's JSON
/// context and therefore cannot register the type itself.
/// </para>
/// <para>
/// Because this writer stages an Outbox message, audit fan-out joins the
/// business unit-of-work: if the business transaction rolls back, the
/// audit integration event is rolled back as well. This is the intended
/// atomicity guarantee. Consumers who want fire-and-forget semantics
/// should implement a custom <see cref="IAuditWriter"/>.
/// </para>
/// </remarks>
[Obsolete("kun.audit.record.v1 已废弃；实体审计请迁移到 Kun.Auditing.AddKunAuditing<TContext>()。", DiagnosticId = "KUNAUDIT001")]
public sealed class EventBusAuditWriter : IAuditWriter
{
    /// <summary>Stable integration-event identifier used when consumers register <see cref="AuditLog"/> with the event-type registry.</summary>
    public const string EventTypeId = "kun.audit.record.v1";

    private readonly IIntegrationEventBus _eventBus;

    /// <summary>Creates a new <see cref="EventBusAuditWriter"/>.</summary>
    public EventBusAuditWriter(IIntegrationEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        _eventBus = eventBus;
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _eventBus.Publish(entry);
        return Task.CompletedTask;
    }
}
