using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace Kun.EventBus;

/// <summary>Fluent builder for populating the <see cref="IEventTypeRegistry"/>.</summary>
public interface IEventTypeRegistrar
{
    /// <summary>Registers a mapping between <paramref name="eventType"/> token and <typeparamref name="TEvent"/>.</summary>
    /// <remarks>
    /// Serialization falls back to reflection-based <c>System.Text.Json</c> defaults. AOT-targeting
    /// consumers should prefer the
    /// <see cref="Register{TEvent}(string, JsonTypeInfo{TEvent})"/> overload instead to thread a
    /// source-generated <see cref="JsonTypeInfo{T}"/> through the outbox.
    /// </remarks>
    IEventTypeRegistrar Register<TEvent>(string eventType) where TEvent : class;

    /// <summary>
    /// Registers a mapping between <paramref name="eventType"/> token and
    /// <typeparamref name="TEvent"/>, plus the source-generated
    /// <see cref="JsonTypeInfo{T}"/> used to serialize outbox payloads without runtime reflection.
    /// </summary>
    /// <param name="eventType">Stable event-id token (e.g. <c>order.placed.v1</c>).</param>
    /// <param name="typeInfo">
    /// JsonTypeInfo for <typeparamref name="TEvent"/>, typically obtained from a
    /// <c>[JsonSerializable(typeof(TEvent))]</c>-decorated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
    /// </param>
    IEventTypeRegistrar Register<TEvent>(string eventType, JsonTypeInfo<TEvent> typeInfo) where TEvent : class;

    /// <summary>
    /// Registers <typeparamref name="TEvent"/> using the token declared on its
    /// <see cref="IntegrationEventIdAttribute"/>. Throws
    /// <see cref="InvalidOperationException"/> when the attribute is absent.
    /// </summary>
    /// <remarks>
    /// Reads the attribute reflectively once at DI-registration time; the resulting
    /// registry is the same O(1) <see cref="EventTypeRegistry"/> used at steady state.
    /// AOT-strict callers can stick with <see cref="Register{TEvent}(string)"/> or
    /// <see cref="Register{TEvent}(string, JsonTypeInfo{TEvent})"/> to avoid the reflection entirely.
    /// </remarks>
    [RequiresUnreferencedCode(
        "Reads [IntegrationEventId] via reflection on typeof(TEvent). " +
        "Under trimming, IntegrationEventIdAttribute instances must be preserved. " +
        "Use Register<TEvent>(string) to avoid reflection entirely.")]
    IEventTypeRegistrar Register<TEvent>() where TEvent : class;
}
