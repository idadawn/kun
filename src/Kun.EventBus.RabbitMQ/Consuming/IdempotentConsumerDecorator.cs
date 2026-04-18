using Microsoft.Extensions.Logging;
using Kun.EventBus.Idempotency;

namespace Kun.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Wraps handler invocation with idempotency enforcement using <see cref="IProcessedMessageStore"/>.
/// The mark is written AFTER the handler succeeds; if the handler throws, no mark is recorded
/// and the message will be redelivered.
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
    /// Executes <paramref name="handler"/> and then marks the message as processed.
    /// If the mark returns false the handler ran twice — logged as a warning (handler must be idempotent).
    /// </summary>
    public async ValueTask ExecuteAsync(
        string consumerName,
        Guid messageId,
        TimeSpan ttl,
        Func<CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken = default)
    {
        // Invoke the handler first; only mark as processed on success.
        await handler(cancellationToken).ConfigureAwait(false);

        var marked = await _store.TryMarkProcessedAsync(consumerName, messageId, ttl, cancellationToken)
            .ConfigureAwait(false);

        if (!marked)
        {
            Log.DuplicateDelivery(_logger, consumerName, messageId);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Duplicate delivery detected after handler execution: consumer={Consumer} messageId={MessageId}")]
        public static partial void DuplicateDelivery(ILogger logger, string consumer, Guid messageId);
    }
}
