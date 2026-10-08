using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.Mediator;

/// <summary>
/// Reflection-free CQRS dispatcher. Handler lookup is O(1) via a pre-built
/// <see cref="FrozenDictionary{TKey,TValue}"/>. All handler types are explicitly
/// registered at startup through <see cref="KunMediatorBuilder"/>, so no runtime
/// scanning or generic type construction is needed — this class is fully AOT-safe.
/// </summary>
public sealed class KunMediator : ISender, IPublisher, IScopedPublisher
{
    private readonly AsyncLocal<DispatchScope?> _current = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FrozenDictionary<Type, RequestHandlerBase> _handlers;

    // Values are NotificationDispatcher<TNotification> instances stored as object
    // to keep the dictionary non-generic. Cast is safe because the builder ensures
    // the value type matches the key exactly.
    private readonly FrozenDictionary<Type, object> _notificationDispatchers;

    internal KunMediator(
        IServiceScopeFactory scopeFactory,
        FrozenDictionary<Type, RequestHandlerBase> handlers,
        FrozenDictionary<Type, object> notificationDispatchers)
    {
        _scopeFactory = scopeFactory;
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
    public async ValueTask<TResponse> Send<TResponse>(
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

        // Create a child scope so that scoped services (DbContext, UoW, etc.) resolve correctly
        // even though KunMediator itself is a singleton. The scope is disposed after each Send.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var previous = _current.Value;
        var current = new DispatchScope(scope.ServiceProvider);
        _current.Value = current;
        try
        {
            return await typed.Handle(request, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            current.Services = null;
            _current.Value = previous;
        }
    }

    /// <summary>
    /// Publishes <paramref name="notification"/> to all handlers registered for
    /// <typeparamref name="TNotification"/>. If no handlers are registered this is a no-op.
    /// Handlers are invoked sequentially in registration order.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!_notificationDispatchers.TryGetValue(typeof(TNotification), out var dispatcherObj))
        {
            return; // no subscribers — no-op
        }

        if (_current.Value?.Services is { } services)
        {
            await PublishInScope(notification, services, cancellationToken).ConfigureAwait(false);
            return;
        }
        await using var scope = _scopeFactory.CreateAsyncScope();
        await PublishInScope(notification, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
    }
    /// <inheritdoc />
    public async ValueTask PublishInScope<TNotification>(TNotification notification, IServiceProvider services,
        CancellationToken cancellationToken = default) where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(services);
        if (!_notificationDispatchers.TryGetValue(typeof(TNotification), out var dispatcher)) return;
        var previous = _current.Value;
        var current = new DispatchScope(services);
        _current.Value = current;
        try
        {
            await ((NotificationDispatcher<TNotification>)dispatcher).PublishAsync(notification, services, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            current.Services = null;
            _current.Value = previous;
        }
    }

    private sealed class DispatchScope(IServiceProvider services)
    {
        public IServiceProvider? Services { get; set; } = services;
    }

}
