namespace Kun.EventBus.Idempotency;

/// <summary>
/// Tracks message IDs that have already been successfully processed by a given consumer,
/// enabling idempotent at-least-once delivery. Implementations are broker-agnostic
/// (Redis, in-memory, SQL).
/// </summary>
public interface IProcessedMessageStore
{
    /// <summary>
    /// Atomically records <paramref name="messageId"/> as processed by <paramref name="consumerName"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the mark succeeded (first processing),
    /// <see langword="false"/> when the message was already processed (duplicate delivery).
    /// </returns>
    ValueTask<bool> TryMarkProcessedAsync(
        string consumerName,
        Guid messageId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
}
