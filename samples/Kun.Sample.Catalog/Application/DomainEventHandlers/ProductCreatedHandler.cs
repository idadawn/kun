using Kun.EventBus;
using Kun.Mediator;
using Kun.Sample.Catalog.Contracts;
using Kun.Sample.Catalog.Domain.Events;

namespace Kun.Sample.Catalog.Application.DomainEventHandlers;

/// <summary>
/// Handles <see cref="ProductCreatedDomainEvent"/> by publishing a
/// <see cref="ProductPublishedIntegrationEvent"/> via the Outbox.
/// Registered as a notification handler on the mediator.
/// </summary>
public sealed class ProductCreatedHandler(IIntegrationEventBus eventBus)
    : INotificationHandler<ProductCreatedNotification>
{
    /// <inheritdoc/>
    public ValueTask Handle(
        ProductCreatedNotification notification,
        CancellationToken cancellationToken)
    {
        var integrationEvent = new ProductPublishedIntegrationEvent(
            notification.Event.ProductId,
            notification.Event.Name,
            notification.Event.Price);

        eventBus.Publish(integrationEvent);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Thin notification wrapper so <see cref="ProductCreatedDomainEvent"/> can be dispatched
/// via <c>IPublisher</c> (which requires <see cref="INotification"/>).
/// </summary>
public sealed record ProductCreatedNotification(ProductCreatedDomainEvent Event) : INotification;
