using Microsoft.Extensions.Logging;
using Kun.EventBus.Idempotency;

namespace Kun.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Wraps handler invocation with idempotency enforcement using <see cref="IProcessedMessageStore"/>.
/// The store is checked BEFORE handler invocation; duplicate deliveries are skipped without
/// invoking the handler. If the handler throws, the mark remains — the message will not be
/// redelivered (at-least-once with idempotent handlers is the expected contract).
/// </summary>
internal sealed partial class IdempotentConsumerDecorator
{
    private readonly IProcessedMessageStore _store;
    private readonly ILogger<IdempotentConsumerDecorator> _logger;

    public IdempotentConsumerDecorator(
        IProcessedMessageStore store,
        ILogger<IdempotentConsumerDecorator> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Marks the message as processed first; if already marked, skips the handler (dedup).
    /// If not yet marked, invokes <paramref name="handler"/>.
    /// </summary>
    public async ValueTask ExecuteAsync(
        string consumerName,
        Guid messageId,
        TimeSpan ttl,
        Func<CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken = default)
    {
        // Mark-first: atomic SET NX returns true only for the first delivery.
        var isFirst = await _store.TryMarkProcessedAsync(consumerName, messageId, ttl, cancellationToken)
            .ConfigureAwait(false);

        if (!isFirst)
        {
            Log.DuplicateDelivery(_logger, consumerName, messageId);
            return;
        }

        await handler(cancellationToken).ConfigureAwait(false);
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Duplicate delivery skipped: consumer={Consumer} messageId={MessageId}")]
        public static partial void DuplicateDelivery(ILogger logger, string consumer, Guid messageId);
    }
}
