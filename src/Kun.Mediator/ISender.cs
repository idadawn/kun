namespace Kun.Mediator;

/// <summary>Dispatches a single request to its registered handler.</summary>
public interface ISender
{
    /// <summary>Send <paramref name="request"/> to its handler and return the response.</summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response produced by the registered handler.</returns>
    ValueTask<TResponse> Send<TResponse>(
        Kun.Core.Cqrs.IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
