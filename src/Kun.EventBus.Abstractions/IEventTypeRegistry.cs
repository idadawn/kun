using System.Text.Json.Serialization.Metadata;

namespace Kun.EventBus;

/// <summary>Maps stable event-type tokens to their CLR types at runtime.</summary>
public interface IEventTypeRegistry
{
    /// <summary>Resolves a CLR type from a stable event-type token.</summary>
    /// <returns><see langword="null"/> when the token is not registered.</returns>
    Type? Resolve(string eventType);

    /// <summary>Resolves the stable token for a CLR type.</summary>
    /// <returns><see langword="null"/> when the type is not registered.</returns>
    string? GetEventType(Type clrType);

    /// <summary>
    /// Enumerates every <c>(eventId, clrType)</c> pair in the registry, ordered by
    /// <c>eventId</c> (ordinal, case-sensitive) for stable output. Intended primarily for
    /// tooling — JSON-Schema export, registry dumps, diagnostic inspection. Consumers on the
    /// hot path should continue to use <see cref="Resolve(string)"/> or
    /// <see cref="GetEventType(Type)"/>.
    /// </summary>
    IReadOnlyCollection<(string EventId, Type ClrType)> GetAll();

    /// <summary>
    /// Returns the source-generated <see cref="JsonTypeInfo"/> registered for
    /// <paramref name="clrType"/> via <see cref="IEventTypeRegistrar.Register{TEvent}(string, JsonTypeInfo{TEvent})"/>.
    /// Returns <see langword="null"/> when the type was registered via the token-only overload
    /// — in that case, callers fall back to reflection-based serialization (not AOT-safe).
    /// </summary>
    JsonTypeInfo? GetJsonTypeInfo(Type clrType);
}
