namespace Kun.EventBus.Background;

/// <summary>事务内领取并持有消息行锁的 Outbox 存储；释放作用域前必须完成批次。</summary>
public interface IOutboxBatchStore : IOutboxMessageStore
{
    /// <summary>提交本批次的发布状态并释放锁；未完成即释放作用域时回滚。</summary>
    Task CompleteBatchAsync(CancellationToken cancellationToken = default);
}
