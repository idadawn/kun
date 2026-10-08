using Kun.EventBus.Idempotency;
using Microsoft.Extensions.Logging;

namespace Kun.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Wraps handler invocation with idempotency enforcement using <see cref="IProcessedMessageStore"/>.
/// 先检查成功记录，只有 handler 成功后才记录；失败或进程中断不留下成功标记。
/// 成功副作用与幂等存储之间仍可能崩溃，写类 handler 必须业务幂等。
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
    /// 跳过已成功的消息；执行成功后写入标记，调用方随后才确认 broker 消息。
    /// </summary>
    public async ValueTask ExecuteAsync(
        string consumerName,
        Guid messageId,
        TimeSpan ttl,
        Func<CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken = default)
    {
        if (await _store.IsProcessedAsync(consumerName, messageId, cancellationToken).ConfigureAwait(false))
        {
            Log.DuplicateDelivery(_logger, consumerName, messageId);
            return;
        }
        await handler(cancellationToken).ConfigureAwait(false);
        await _store.TryMarkProcessedAsync(consumerName, messageId, ttl, cancellationToken).ConfigureAwait(false);
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
