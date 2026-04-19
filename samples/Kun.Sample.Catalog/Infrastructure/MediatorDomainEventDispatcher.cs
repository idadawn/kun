using Kun.Core.Domain;
using Kun.Mediator;
using Kun.Sample.Catalog.Application.DomainEventHandlers;
using Kun.Sample.Catalog.Domain.Events;

namespace Kun.Sample.Catalog.Infrastructure;

/// <summary>
/// Bridges the EF Core <see cref="IDomainEventDispatcher"/> contract to
/// <see cref="IPublisher"/> so domain events raised inside aggregates are
/// dispatched to their in-process notification handlers after each commit.
/// </summary>
public sealed class MediatorDomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    /// <inheritdoc/>
    public async ValueTask DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            switch (domainEvent)
            {
                case ProductCreatedDomainEvent e:
                    await publisher.Publish(new ProductCreatedNotification(e), cancellationToken);
                    break;

                // Other domain events that do not need cross-service publishing are
                // silently ignored here; add cases as the domain grows.
                default:
                    break;
            }
        }
    }
}
