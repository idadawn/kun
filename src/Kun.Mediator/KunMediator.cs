using System.Collections.Frozen;

namespace Kun.Mediator;

/// <summary>
/// Reflection-free CQRS dispatcher. Handler lookup is O(1) via a pre-built
/// <see cref="FrozenDictionary{TKey,TValue}"/>. All handler types are explicitly
/// registered at startup through <see cref="KunMediatorBuilder"/>, so no runtime
/// scanning or generic type construction is needed — this class is fully AOT-safe.
/// </summary>
public sealed class KunMediator : ISender, IPublisher
{
    private readonly IServiceProvider _services;
    private readonly FrozenDictionary<Type, RequestHandlerBase> _handlers;

    // Values are NotificationDispatcher<TNotification> instances stored as object
    // to keep the dictionary non-generic. Cast is safe because the builder ensures
    // the value type matches the key exactly.
    private readonly FrozenDictionary<Type, object> _notificationDispatchers;

    internal KunMediator(
        IServiceProvider services,
        FrozenDictionary<Type, RequestHandlerBase> handlers,
        FrozenDictionary<Type, object> notificationDispatchers)
    {
        _services = services;
        _handlers = handlers;
        _notificationDispatchers = notificationDispatchers;
    }

    /// <summary>
    /// Sends <paramref name="request"/> to its registered handler, executing any
    /// pipeline behaviors registered for <typeparamref name="TResponse"/> along the way.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="request">The request to dispatch. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response produced by the registered handler.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no handler is registered for <paramref name="request"/>'s concrete type.
    /// </exception>
    public ValueTask<TResponse> Send<TResponse>(
        Kun.Core.Cqrs.IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        if (!_handlers.TryGetValue(requestType, out var wrapperBase))
        {
            throw new InvalidOperationException(
                $"No handler registered for request type '{requestType.FullName}'. " +
                $"Register it via AddKunMediator(b => b.AddRequestHandler<...>()).");
        }

        // The wrapper was built as RequestHandlerWrapper<TReq, TResponse> where TReq is the
        // concrete runtime type and TResponse matches the call-site TResponse. The cast to
        // IRequestHandlerWrapper<TResponse> is therefore safe — no MakeGenericMethod needed.
        var typed = (IRequestHandlerWrapper<TResponse>)wrapperBase;
        return typed.Handle(request, _services, cancellationToken);
    }

    /// <summary>
    /// Publishes <paramref name="notification"/> to all handlers registered for
    /// <typeparamref name="TNotification"/>. If no handlers are registered this is a no-op.
    /// Handlers are invoked sequentially in registration order.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!_notificationDispatchers.TryGetValue(typeof(TNotification), out var dispatcherObj))
        {
            return ValueTask.CompletedTask; // no subscribers — no-op
        }

        var dispatcher = (NotificationDispatcher<TNotification>)dispatcherObj;
        return dispatcher.PublishAsync(notification, _services, cancellationToken);
    }
}
