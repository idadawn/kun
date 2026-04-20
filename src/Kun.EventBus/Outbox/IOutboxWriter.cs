namespace Kun.EventBus.Outbox;

/// <summary>
/// Writer primitive for enqueuing integration events from non-aggregate collaborators
/// (e.g. audit interceptors, storage-event adapters) inside the current DbContext
/// <c>SaveChangesAsync</c> transaction, preserving outbox atomicity.
/// </summary>
/// <remarks>
/// <para>
/// The DI container resolves <see cref="IOutboxWriter"/> to the same scoped instance as
/// <see cref="IIntegrationEventBus"/> — both interfaces name the same accumulator. Prefer
/// <see cref="IOutboxWriter"/> when the caller is an infrastructure collaborator (interceptor,
/// adapter) rather than a domain aggregate or command handler; the narrower contract
/// communicates intent and decouples from the full bus surface.
/// </para>
/// </remarks>
public interface IOutboxWriter
{
    /// <summary>
    /// Enqueues an integration event for atomic commit with the pending DbContext change set.
    /// Produces the same <see cref="OutboxMessage"/> shape as
    /// <see cref="IIntegrationEventBus.Publish{TEvent}(TEvent)"/>: stable event id via
    /// <see cref="IEventTypeRegistry"/>, current tenant and trace context captured.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type. Must be registered via <see cref="IEventTypeRegistrar"/>.</typeparam>
    /// <param name="event">The event instance to enqueue. Must not be <see langword="null"/>.</param>
    void Enqueue<TEvent>(TEvent @event) where TEvent : class;
}
