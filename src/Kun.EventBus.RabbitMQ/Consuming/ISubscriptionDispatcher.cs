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
    ValueTask DispatchAsync(
        Type clrType,
        string payload,
        MessageContext context,
        CancellationToken cancellationToken);
}
