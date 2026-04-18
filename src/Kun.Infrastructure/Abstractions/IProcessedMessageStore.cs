namespace Kun.Infrastructure.Abstractions;

/// <summary>
/// 消费端已处理消息记录存储。用于保证 Outbox 至少一次投递场景下的消费幂等。
/// <para>
/// 默认实现基于 Redis 的 <c>SETNX + EX</c>（单命令原子操作）。
/// 业务可替换为基于数据库唯一索引的实现以获得跨故障域的强一致性。
/// </para>
/// </summary>
public interface IProcessedMessageStore
{
    /// <summary>
    /// 尝试把 <paramref name="messageId"/> 标记为"正在处理"。
    /// </summary>
    /// <returns>
    /// <see langword="true"/> 表示首次处理，可以继续执行 Handler；
    /// <see langword="false"/> 表示消息已被处理过，Handler 应直接跳过。
    /// </returns>
    Task<bool> TryMarkAsProcessingAsync(
        Guid messageId,
        string consumerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handler 成功完成后调用。可选地把状态从"处理中"升级为"已完成"，
    /// 便于运维排查长时间卡住的消息。
    /// </summary>
    Task MarkAsCompletedAsync(
        Guid messageId,
        string consumerName,
        CancellationToken cancellationToken = default);
}
