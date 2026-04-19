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
}
