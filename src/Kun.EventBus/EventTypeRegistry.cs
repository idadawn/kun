using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization.Metadata;

namespace Kun.EventBus;

/// <summary>Immutable, O(1) event-type registry backed by <see cref="FrozenDictionary{TKey,TValue}"/>.</summary>
public sealed class EventTypeRegistry : IEventTypeRegistry
{
    private readonly FrozenDictionary<string, Type> _tokenToType;
    private readonly FrozenDictionary<Type, string> _typeToToken;
    private readonly FrozenDictionary<Type, JsonTypeInfo> _typeToJsonTypeInfo;
    private readonly ReadOnlyCollection<(string EventId, Type ClrType)> _sortedAll;

    internal EventTypeRegistry(Dictionary<string, Type> tokenToType)
        : this(tokenToType, jsonTypeInfos: null)
    {
    }

    internal EventTypeRegistry(
        Dictionary<string, Type> tokenToType,
        Dictionary<Type, JsonTypeInfo>? jsonTypeInfos)
    {
        _tokenToType = tokenToType.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _typeToToken = tokenToType
            .ToDictionary(kv => kv.Value, kv => kv.Key)
            .ToFrozenDictionary();

        _typeToJsonTypeInfo = (jsonTypeInfos ?? new Dictionary<Type, JsonTypeInfo>())
            .ToFrozenDictionary();

        // Pre-sort so GetAll() can return a stable ordering without per-call allocation.
        // Wrap in ReadOnlyCollection so a consumer downcasting via the declared
        // IReadOnlyCollection return type cannot mutate the underlying array.
        var sorted = tokenToType
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value))
            .ToArray();
        _sortedAll = new ReadOnlyCollection<(string EventId, Type ClrType)>(sorted);
    }

    /// <inheritdoc/>
    public Type? Resolve(string eventType) =>
        _tokenToType.TryGetValue(eventType, out var t) ? t : null;

    /// <inheritdoc/>
    public string? GetEventType(Type clrType) =>
        _typeToToken.TryGetValue(clrType, out var token) ? token : null;

    /// <inheritdoc/>
    public IReadOnlyCollection<(string EventId, Type ClrType)> GetAll() => _sortedAll;

    /// <inheritdoc/>
    public JsonTypeInfo? GetJsonTypeInfo(Type clrType) =>
        _typeToJsonTypeInfo.TryGetValue(clrType, out var info) ? info : null;
}
