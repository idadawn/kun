using System.Diagnostics.CodeAnalysis;
using Kun.EventBus.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Kun.EventBus.DependencyInjection;

/// <summary>Fluent builder for configuring the Kun event bus.</summary>
public sealed class KunEventBusBuilder : IEventTypeRegistrar
{
    private readonly IServiceCollection _services;
    internal readonly Dictionary<string, Type> EventTypeMap = [];

    internal KunEventBusBuilder(IServiceCollection services) => _services = services;

    /// <inheritdoc/>
    public IEventTypeRegistrar Register<TEvent>(string eventType) where TEvent : class
    {
        EventTypeMap[eventType] = typeof(TEvent);
        return this;
    }

    /// <inheritdoc/>
    [RequiresUnreferencedCode(
        "Reads [IntegrationEventId] via reflection on typeof(TEvent). " +
        "Under trimming, IntegrationEventIdAttribute instances must be preserved. " +
        "Use Register<TEvent>(string) to avoid reflection entirely.")]
    public IEventTypeRegistrar Register<TEvent>() where TEvent : class
    {
        var attr = (IntegrationEventIdAttribute?)Attribute.GetCustomAttribute(
            typeof(TEvent), typeof(IntegrationEventIdAttribute), inherit: false);
        if (attr is null)
        {
            throw new InvalidOperationException(
                $"Type {typeof(TEvent).FullName} has no [IntegrationEventId] attribute. " +
                $"Decorate it with [IntegrationEventId(\"your.token.v1\")] " +
                $"or call Register<{typeof(TEvent).Name}>(\"your.token.v1\") explicitly.");
        }

        return Register<TEvent>(attr.EventType);
    }

    /// <summary>Registers an <see cref="IIntegrationEventHandler{TEvent}"/> for <typeparamref name="TEvent"/>.</summary>
    public KunEventBusBuilder AddHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        _services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return this;
    }

    /// <summary>Registers the in-memory idempotency store (testing only).</summary>
    public KunEventBusBuilder UseInMemoryIdempotency()
    {
        _services.TryAddSingleton<IProcessedMessageStore, InMemoryProcessedMessageStore>();
        return this;
    }

    /// <summary>
    /// Registers the Redis idempotency store.
    /// The caller must register <see cref="IConnectionMultiplexer"/> separately
    /// (e.g. via <c>services.AddSingleton&lt;IConnectionMultiplexer&gt;(ConnectionMultiplexer.Connect(...))</c>).
    /// </summary>
    public KunEventBusBuilder UseRedisIdempotency()
    {
        _services.TryAddSingleton<IProcessedMessageStore, RedisProcessedMessageStore>();
        return this;
    }
}
