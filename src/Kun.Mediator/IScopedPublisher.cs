namespace Kun.Mediator;

/// <summary>由事务宿主显式指定作用域，保证通知处理器复用同一 DbContext 与 Outbox 缓冲。</summary>
public interface IScopedPublisher
{
    /// <summary>在调用方提供的作用域内顺序派发通知；不创建或释放该作用域。</summary>
    ValueTask PublishInScope<TNotification>(TNotification notification, IServiceProvider services,
        CancellationToken cancellationToken = default) where TNotification : INotification;
}
