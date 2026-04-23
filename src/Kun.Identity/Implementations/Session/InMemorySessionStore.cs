using System.Collections.Concurrent;
using Kun.Identity.Abstractions.Session;

namespace Kun.Identity.Implementations.Session;

/// <summary>
/// Process-local <see cref="ISessionStore"/> backed by a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/>. Suitable for
/// development loops and unit tests that do not want to pull a Redis
/// container into the fixture. For production deployments use
/// <c>RedisSessionStore</c> (cross-process, backed by
/// <c>Kun.Caching.IKunCache</c>).
/// </summary>
/// <remarks>
/// <para>
/// Entries expire lazily: every <see cref="GetAsync"/> call prunes an
/// entry that has passed its absolute expiry before returning
/// <see langword="null"/>. A callers-paid sweep means the backing
/// dictionary can temporarily hold unreachable entries between the
/// expiry instant and the next read — acceptable for the in-memory
/// store because the data is bounded by active user sessions anyway.
/// </para>
/// <para>
/// Instances are thread-safe. There is no eviction cap; consumers
/// expecting very large session counts should prefer the Redis
/// implementation.
/// </para>
/// </remarks>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates an instance that reads the current time from the ambient <see cref="TimeProvider.System"/>.</summary>
    public InMemorySessionStore()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates an instance backed by the supplied <paramref name="timeProvider"/>.
    /// Tests use this overload to drive expiry deterministically.
    /// </summary>
    public InMemorySessionStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<SessionData?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        if (!_store.TryGetValue(sessionId, out var entry))
        {
            return Task.FromResult<SessionData?>(null);
        }

        if (entry.IsExpired(_timeProvider))
        {
            _store.TryRemove(KeyValuePair.Create(sessionId, entry));
            return Task.FromResult<SessionData?>(null);
        }

        return Task.FromResult<SessionData?>(entry.Data);
    }

    /// <inheritdoc />
    public Task SetAsync(string sessionId, SessionData data, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(data);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "Session TTL must be positive.");
        }

        var expiresAt = _timeProvider.GetUtcNow().Add(ttl);
        _store[sessionId] = new Entry(data, expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    private sealed record Entry(SessionData Data, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired(TimeProvider clock) => clock.GetUtcNow() >= ExpiresAt;
    }
}
