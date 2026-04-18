namespace Kun.Mediator;

/// <summary>Delegate passed to a pipeline behavior representing the next step in the chain.</summary>
/// <typeparam name="TResponse">The response type returned by the handler.</typeparam>
#pragma warning disable CA1711 // "Delegate" suffix is intentional — this IS a delegate type
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();
#pragma warning restore CA1711
