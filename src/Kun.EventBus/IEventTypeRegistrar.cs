namespace Kun.EventBus;

/// <summary>Fluent builder for populating the <see cref="IEventTypeRegistry"/>.</summary>
public interface IEventTypeRegistrar
{
    /// <summary>Registers a mapping between <paramref name="eventType"/> token and <typeparamref name="TEvent"/>.</summary>
    IEventTypeRegistrar Register<TEvent>(string eventType) where TEvent : class;
}
