using System.Collections.Concurrent;

namespace Kun.EventBus.Idempotency;

/// <summary>
/// In-memory implementation of <see cref="IProcessedMessageStore"/> backed by a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Intended for testing and
/// single-instance scenarios only — state is lost on restart and is not shared
/// across multiple processes.
/// </summary>
public sealed class InMemoryProcessedMessageStore : IProcessedMessageStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _store = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises the store using the supplied <paramref name="timeProvider"/>.</summary>
    public InMemoryProcessedMessageStore(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <summary>Initialises the store using <see cref="TimeProvider.System"/>.</summary>
    public InMemoryProcessedMessageStore() : this(TimeProvider.System) { }

    /// <inheritdoc/>
    public ValueTask<bool> TryMarkProcessedAsync(
        string consumerName,
        Guid messageId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var key = $"{consumerName}:{messageId}";
        var expiry = _timeProvider.GetUtcNow() + ttl;

        // Optimistic path: try to add the entry atomically.
        if (_store.TryAdd(key, expiry))
        {
            return ValueTask.FromResult(true);
        }

        // Entry already exists — check whether it has expired.
        if (_store.TryGetValue(key, out var existingExpiry))
        {
            if (_timeProvider.GetUtcNow() >= existingExpiry)
            {
                // Expired: overwrite with the new expiry and treat as a first-time mark.
                _store[key] = expiry;
                return ValueTask.FromResult(true);
            }
        }

        // Still valid — duplicate delivery.
        return ValueTask.FromResult(false);
    }
}
