using System.Collections.Frozen;

namespace Kun.EventBus;

/// <summary>Immutable, O(1) event-type registry backed by <see cref="FrozenDictionary{TKey,TValue}"/>.</summary>
public sealed class EventTypeRegistry : IEventTypeRegistry
{
    private readonly FrozenDictionary<string, Type> _tokenToType;
    private readonly FrozenDictionary<Type, string> _typeToToken;
    private readonly (string EventId, Type ClrType)[] _sortedAll;

    internal EventTypeRegistry(Dictionary<string, Type> tokenToType)
    {
        _tokenToType = tokenToType.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _typeToToken = tokenToType
            .ToDictionary(kv => kv.Value, kv => kv.Key)
            .ToFrozenDictionary();

        // Pre-sort so GetAll() can return a stable ordering without per-call allocation.
        _sortedAll = tokenToType
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value))
            .ToArray();
    }

    /// <inheritdoc/>
    public Type? Resolve(string eventType) =>
        _tokenToType.TryGetValue(eventType, out var t) ? t : null;

    /// <inheritdoc/>
    public string? GetEventType(Type clrType) =>
        _typeToToken.TryGetValue(clrType, out var token) ? token : null;

    /// <inheritdoc/>
    public IReadOnlyCollection<(string EventId, Type ClrType)> GetAll() => _sortedAll;
}
