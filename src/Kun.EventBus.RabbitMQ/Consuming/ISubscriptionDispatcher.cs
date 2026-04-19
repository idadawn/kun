namespace Kun.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Dispatches a deserialized integration event payload to the appropriate
/// <see cref="IIntegrationEventHandler{TEvent}"/>. One implementation is registered
/// per subscription, avoiding <c>MakeGenericType</c> at runtime.
/// </summary>
internal interface ISubscriptionDispatcher
{
    /// <summary>The CLR type this dispatcher handles.</summary>
    Type EventClrType { get; }

    /// <summary>Deserializes <paramref name="payload"/> and invokes the handler.</summary>
    /// <param name="clrType">The CLR type to dispatch to.</param>
    /// <param name="payload">The raw JSON payload.</param>
    /// <param name="context">The message context.</param>
    /// <param name="scopedServices">
    /// The per-message scoped <see cref="IServiceProvider"/> from which the handler is resolved.
    /// Must not be the root provider — handlers are typically registered as scoped.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask DispatchAsync(
        Type clrType,
        string payload,
        MessageContext context,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken);
}
