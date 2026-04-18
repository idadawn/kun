using Kun.EventBus.Outbox;

namespace Kun.EventBus;

/// <summary>
/// Broker-specific transport that publishes a single <see cref="OutboxMessage"/> to the message bus.
/// Implementations live in broker adapters (e.g. <c>Kun.EventBus.RabbitMQ</c>).
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>Publishes <paramref name="message"/> to the broker.</summary>
    /// <param name="message">The persisted outbox envelope to dispatch.</param>
    /// <param name="cancellationToken">Cancels the publish operation.</param>
    /// <exception cref="Exception">Thrown when publish fails; the caller should record the failure on the outbox row.</exception>
    ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
