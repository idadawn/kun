namespace Kun.EventBus;

/// <summary>High-level façade for publishing integration events via the Outbox pattern.</summary>
public interface IIntegrationEventBus
{
    /// <summary>Stages <paramref name="event"/> as an outbox message within the current unit-of-work.</summary>
    void Publish<TEvent>(TEvent @event) where TEvent : class;
}
