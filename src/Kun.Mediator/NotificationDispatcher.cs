namespace Kun.Mediator;

/// <summary>
/// Holds all handler factories for a single <see cref="INotification"/> type and
/// dispatches to them sequentially. Stored as <c>object</c> in a
/// <c>FrozenDictionary&lt;Type, object&gt;</c> so the dictionary stays non-generic.
/// </summary>
/// <typeparam name="TNotification">The notification type this dispatcher handles.</typeparam>
internal sealed class NotificationDispatcher<TNotification>
    where TNotification : INotification
{
    private readonly IReadOnlyList<Func<IServiceProvider, INotificationHandler<TNotification>>> _handlerFactories;

    /// <summary>Initialises the dispatcher with the given handler factories.</summary>
    public NotificationDispatcher(
        IReadOnlyList<Func<IServiceProvider, INotificationHandler<TNotification>>> handlerFactories)
    {
        _handlerFactories = handlerFactories;
    }

    /// <summary>Invokes all registered handlers sequentially, awaiting each before proceeding.</summary>
    public async ValueTask PublishAsync(
        TNotification notification,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        foreach (var factory in _handlerFactories)
        {
            var handler = factory(services);
            await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}
