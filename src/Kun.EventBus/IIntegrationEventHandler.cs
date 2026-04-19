namespace Kun.EventBus;

/// <summary>Handles a strongly-typed integration event received from the broker.</summary>
/// <typeparam name="TEvent">The integration event type.</typeparam>
#pragma warning disable CA1711 // Identifier should not have incorrect suffix — "EventHandler" suffix is intentional for domain clarity
public interface IIntegrationEventHandler<in TEvent> where TEvent : class
#pragma warning restore CA1711
{
    /// <summary>Processes the <paramref name="event"/>.</summary>
    ValueTask HandleAsync(TEvent @event, MessageContext context, CancellationToken cancellationToken = default);
}
