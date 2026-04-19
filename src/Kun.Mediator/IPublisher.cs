namespace Kun.Mediator;

/// <summary>Marker interface for cross-cutting notifications broadcast to many handlers.</summary>
public interface INotification { }

/// <summary>Handler for a specific <see cref="INotification"/>.</summary>
/// <typeparam name="TNotification">The notification type this handler processes.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Handles the notification.</summary>
    /// <param name="notification">The notification instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask Handle(TNotification notification, CancellationToken cancellationToken);
}

/// <summary>Publishes a notification to all registered handlers.</summary>
public interface IPublisher
{
    /// <summary>Publish <paramref name="notification"/> to all registered handlers sequentially.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
