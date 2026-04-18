namespace Kun.Mediator;

/// <summary>
/// Pipeline behavior wrapping a <see cref="Kun.Core.Cqrs.IRequestHandler{TRequest,TResponse}"/>.
/// Behaviors form a nested "onion": outer behaviors see both the pre- and post-handler phases
/// of all inner behaviors.
/// </summary>
/// <typeparam name="TRequest">The request type handled by this behavior.</typeparam>
/// <typeparam name="TResponse">The response type produced by this behavior.</typeparam>
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : Kun.Core.Cqrs.IRequest<TResponse>
{
    /// <summary>
    /// Handles the request by optionally performing work before and/or after invoking
    /// <paramref name="next"/>.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="next">The delegate representing the next step (inner behavior or handler).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the inner pipeline.</returns>
    ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
