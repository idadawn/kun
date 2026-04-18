namespace Kun.Core.Domain;

/// <summary>
/// 标记领域事件。领域事件由聚合根内部产生，表示业务上发生的重要事实。
/// 领域事件仅在聚合根边界内同步分发；若需跨服务传递，请在事件处理器中
/// 转换为 <see cref="IIntegrationEvent"/>。
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// 事件发生的 UTC 时间戳。
    /// </summary>
    DateTime OccurredAt { get; }
}
