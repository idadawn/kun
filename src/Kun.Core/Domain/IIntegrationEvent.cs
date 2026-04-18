namespace Kun.Core.Domain;

/// <summary>
/// 集成事件标记接口。集成事件用于跨服务、跨进程的异步通知，
/// 与领域事件不同，集成事件通过 Outbox + 消息总线传递，具备版本演进能力。
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>消息唯一标识，用于消费端幂等去重。</summary>
    Guid EventId { get; }

    /// <summary>事件发生的 UTC 时间戳。</summary>
    DateTime OccurredAt { get; }
}
