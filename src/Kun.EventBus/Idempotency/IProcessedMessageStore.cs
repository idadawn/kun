namespace Kun.EventBus.Idempotency;

/// <summary>
/// Tracks message IDs that have already been successfully processed by a given consumer,
/// enabling idempotent at-least-once delivery. Implementations are broker-agnostic
/// (Redis, in-memory, SQL).
/// </summary>
public interface IProcessedMessageStore
{
    /// <summary>查询成功记录；旧自定义实现默认返回 false，保留至少一次语义，需升级实现以消除重复。</summary>
    ValueTask<bool> IsProcessedAsync(string consumerName, Guid messageId,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

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
