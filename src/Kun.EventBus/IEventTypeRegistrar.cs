using System.Diagnostics.CodeAnalysis;

namespace Kun.EventBus;

/// <summary>Fluent builder for populating the <see cref="IEventTypeRegistry"/>.</summary>
public interface IEventTypeRegistrar
{
    /// <summary>Registers a mapping between <paramref name="eventType"/> token and <typeparamref name="TEvent"/>.</summary>
    IEventTypeRegistrar Register<TEvent>(string eventType) where TEvent : class;

    /// <summary>
    /// Registers <typeparamref name="TEvent"/> using the token declared on its
    /// <see cref="IntegrationEventIdAttribute"/>. Throws
    /// <see cref="InvalidOperationException"/> when the attribute is absent.
    /// </summary>
    /// <remarks>
    /// Reads the attribute reflectively once at DI-registration time; the resulting
    /// registry is the same O(1) <see cref="EventTypeRegistry"/> used at steady state.
    /// AOT-strict callers can stick with <see cref="Register{TEvent}(string)"/> to
    /// avoid the reflection entirely.
    /// </remarks>
    [RequiresUnreferencedCode(
        "Reads [IntegrationEventId] via reflection on typeof(TEvent). " +
        "Under trimming, IntegrationEventIdAttribute instances must be preserved. " +
        "Use Register<TEvent>(string) to avoid reflection entirely.")]
    IEventTypeRegistrar Register<TEvent>() where TEvent : class;
}
