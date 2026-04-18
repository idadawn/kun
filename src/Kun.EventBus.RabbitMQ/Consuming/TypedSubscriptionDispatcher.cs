using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.EventBus.RabbitMQ.Consuming;

/// <summary>
/// AOT-safe dispatcher for a specific event type <typeparamref name="TEvent"/>.
/// Registered once per subscription so that handler resolution and deserialization
/// are fully generic at compile time — no <c>MakeGenericType</c> or runtime reflection.
/// </summary>
/// <typeparam name="TEvent">The strongly-typed integration event.</typeparam>
internal sealed class TypedSubscriptionDispatcher<TEvent> : ISubscriptionDispatcher
    where TEvent : class
{
    private readonly IServiceProvider _services;
    private readonly JsonTypeInfo<TEvent> _typeInfo;

    public TypedSubscriptionDispatcher(
        IServiceProvider services,
        JsonTypeInfo<TEvent> typeInfo)
    {
        _services = services;
        _typeInfo = typeInfo;
    }

    /// <inheritdoc/>
    public Type EventClrType => typeof(TEvent);

    /// <inheritdoc/>
    public async ValueTask DispatchAsync(
        Type clrType,
        string payload,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        if (clrType != typeof(TEvent))
        {
            throw new InvalidOperationException(
                $"TypedSubscriptionDispatcher<{typeof(TEvent).Name}> cannot dispatch type {clrType.Name}.");
        }

        var @event = JsonSerializer.Deserialize(payload, _typeInfo)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize payload as {typeof(TEvent).Name}.");

        var handler = _services.GetRequiredService<IIntegrationEventHandler<TEvent>>();
        await handler.HandleAsync(@event, context, cancellationToken).ConfigureAwait(false);
    }
}
