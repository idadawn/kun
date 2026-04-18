namespace Kun.Mediator;

/// <summary>
/// Internal interface that unifies closed-generic handler wrappers by response type,
/// allowing <see cref="KunMediator"/> to dispatch without runtime <c>MakeGenericMethod</c>.
/// </summary>
internal interface IRequestHandlerWrapper<TResponse>
{
    /// <summary>Executes the full pipeline (behaviors + handler) for the given request.</summary>
    ValueTask<TResponse> Handle(
        Kun.Core.Cqrs.IRequest<TResponse> request,
        IServiceProvider services,
        CancellationToken cancellationToken);
}
