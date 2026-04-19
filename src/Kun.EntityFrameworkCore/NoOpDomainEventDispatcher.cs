using Kun.Core.Domain;

namespace Kun.EntityFrameworkCore;

/// <summary>
/// Default no-op domain event dispatcher. Users replace this with a real implementation
/// (e.g. one backed by <c>Kun.Mediator.IPublisher</c>) by registering their own
/// <see cref="IDomainEventDispatcher"/> before calling <c>AddKunEntityFrameworkCore</c>.
/// </summary>
public sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    /// <inheritdoc/>
    public ValueTask DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
