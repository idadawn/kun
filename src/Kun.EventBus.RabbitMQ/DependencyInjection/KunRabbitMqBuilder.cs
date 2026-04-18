using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Kun.EventBus.RabbitMQ.Consuming;

namespace Kun.EventBus.RabbitMQ.DependencyInjection;

/// <summary>Fluent builder for configuring RabbitMQ subscriptions and handlers.</summary>
public sealed class KunRabbitMqBuilder
{
    private readonly IServiceCollection _services;

    internal KunRabbitMqBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the handler for <typeparamref name="TEvent"/>
    /// and records the subscription so the background subscriber declares the queue at startup.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="eventTypeId">Stable event type ID, e.g. <c>order.placed.v1</c>.</param>
    /// <param name="typeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
    /// Obtain from your <c>JsonSerializerContext</c>, e.g. <c>MyJsonContext.Default.OrderPlacedIntegrationEvent</c>.
    /// </param>
    public KunRabbitMqBuilder Subscribe<TEvent, THandler>(
        string eventTypeId,
        JsonTypeInfo<TEvent> typeInfo)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        // Register the handler.
        _services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();

        // Register the AOT-safe dispatcher for this event type.
        _services.AddSingleton<ISubscriptionDispatcher>(sp =>
            new TypedSubscriptionDispatcher<TEvent>(sp, typeInfo));

        // Record subscription metadata for queue declaration at startup.
        _services.AddSingleton(new SubscriptionRegistration
        {
            EventTypeId = eventTypeId,
            EventClrType = typeof(TEvent),
        });

        return this;
    }
}
