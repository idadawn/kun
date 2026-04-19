using Kun.EventBus.Outbox;

namespace Kun.EventBus;

/// <summary>Dispatches pending outbox messages to the underlying broker.</summary>
public interface IOutboxPublisher
{
    /// <summary>Publishes all <see cref="OutboxStatus.Pending"/> messages to the broker.</summary>
    ValueTask PublishPendingAsync(CancellationToken cancellationToken = default);
}
