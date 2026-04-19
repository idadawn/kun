namespace Kun.Mediator;

/// <summary>
/// Closed-generic wrapper that holds compile-time-closed factories for one handler
/// and its associated pipeline behaviors. All generics are resolved at registration
/// time — no <c>MakeGenericType</c> or reflection occurs at dispatch time.
/// </summary>
/// <typeparam name="TRequest">The concrete request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal sealed class RequestHandlerWrapper<TRequest, TResponse>
    : RequestHandlerBase, IRequestHandlerWrapper<TResponse>
    where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
{
    private readonly Func<IServiceProvider, Kun.Core.Cqrs.IRequestHandler<TRequest, TResponse>> _handlerFactory;
    private readonly IReadOnlyList<Func<IServiceProvider, IPipelineBehavior<TRequest, TResponse>>> _behaviorFactories;

    /// <summary>
    /// Initialises a new wrapper with the given handler and behavior factories.
    /// All factories are compile-time-closed generics produced by
    /// <see cref="KunMediatorBuilder"/> — zero runtime reflection.
    /// </summary>
    public RequestHandlerWrapper(
        Func<IServiceProvider, Kun.Core.Cqrs.IRequestHandler<TRequest, TResponse>> handlerFactory,
        IReadOnlyList<Func<IServiceProvider, IPipelineBehavior<TRequest, TResponse>>> behaviorFactories)
    {
        _handlerFactory = handlerFactory;
        _behaviorFactories = behaviorFactories;
    }

    /// <inheritdoc/>
    public ValueTask<TResponse> Handle(
        Kun.Core.Cqrs.IRequest<TResponse> request,
        IServiceProvider services,
        CancellationToken cancellationToken)
        => HandleTyped((TRequest)request, services, cancellationToken);

    private ValueTask<TResponse> HandleTyped(TRequest request, IServiceProvider services, CancellationToken ct)
    {
        var handler = _handlerFactory(services);

        // Seed the pipeline with the innermost step: the actual handler.
        RequestHandlerDelegate<TResponse> next = () => handler.Handle(request, ct);

        // Build the onion from innermost (handler) outward by iterating behaviors in reverse.
        for (var i = _behaviorFactories.Count - 1; i >= 0; i--)
        {
            var behavior = _behaviorFactories[i](services);
            var capturedNext = next; // capture loop variable
            next = () => behavior.Handle(request, capturedNext, ct);
        }

        return next();
    }
}
