namespace Kun.Core.Domain;

/// <summary>
/// Dispatches domain events to their in-process handlers.
/// <para>
/// Called by the Unit of Work immediately before persistence commits, so handlers run inside
/// the same EF Core transaction that produced the aggregate changes. For cross-service events,
/// handlers translate the domain event into an <see cref="IIntegrationEvent"/> and enqueue it
/// via the Outbox.
/// </para>
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>Dispatch the given domain events to all registered handlers, in insertion order.</summary>
    ValueTask DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default);
}
