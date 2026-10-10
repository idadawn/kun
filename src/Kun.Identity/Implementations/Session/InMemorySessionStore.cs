using Kun.Identity.Abstractions;
using Kun.Identity.Abstractions.Session;

namespace Kun.Identity.Implementations.Session;

/// <summary>Process-local session registry for tests/development; all index, renewal and revocation changes share a lock.</summary>
public sealed class InMemorySessionStore : ISessionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _store = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _users = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Purpose, string Nonce), DateTimeOffset> _nonces = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a registry using the system clock.</summary>
    public InMemorySessionStore() : this(TimeProvider.System) { }

    /// <summary>Creates a registry with an explicit clock.</summary>
    public InMemorySessionStore(TimeProvider timeProvider) { ArgumentNullException.ThrowIfNull(timeProvider); _timeProvider = timeProvider; }

    /// <inheritdoc />
    public Task<SessionData?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        lock (_gate)
        {
            if (!_store.TryGetValue(sessionId, out var entry)) return Task.FromResult<SessionData?>(null);
            if (entry.ExpiresAt <= _timeProvider.GetUtcNow()) { Remove(sessionId); return Task.FromResult<SessionData?>(null); }
            return Task.FromResult<SessionData?>(entry.Data);
        }
    }

    /// <inheritdoc />
    public Task SetAsync(string sessionId, SessionData data, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId); ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(data.UserId); ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        lock (_gate)
        {
            Remove(sessionId);
            _store[sessionId] = new Entry(data, _timeProvider.GetUtcNow() + ttl);
            if (!_users.TryGetValue(data.UserId, out var ids)) _users[data.UserId] = ids = new HashSet<string>(StringComparer.Ordinal);
            ids.Add(sessionId);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId); lock (_gate) Remove(sessionId); return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RenewAsync(string sessionId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId); ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        lock (_gate)
        {
            if (!_store.TryGetValue(sessionId, out var entry)) return Task.FromResult(false);
            if (entry.ExpiresAt <= _timeProvider.GetUtcNow()) { Remove(sessionId); return Task.FromResult(false); }
            _store[sessionId] = entry with { ExpiresAt = _timeProvider.GetUtcNow() + ttl };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<int> RevokeUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        lock (_gate)
        {
            var ids = _users.TryGetValue(userId, out var sessions) ? sessions.ToArray() : [];
            foreach (var id in ids) Remove(id);
            return Task.FromResult(ids.Length);
        }
    }

    /// <inheritdoc />
    public Task<int> ApplyLogoutAsync(OidcLogoutToken logout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logout);
        ArgumentException.ThrowIfNullOrWhiteSpace(logout.Issuer); ArgumentException.ThrowIfNullOrWhiteSpace(logout.TokenId);
        if (string.IsNullOrWhiteSpace(logout.Subject) && string.IsNullOrWhiteSpace(logout.SessionId)) throw new ArgumentException("Logout requires a subject or provider session.", nameof(logout));
        lock (_gate)
        {
            if (!UseNonce("logout:" + logout.Issuer, logout.TokenId, logout.ExpiresAt.AddSeconds(30) - _timeProvider.GetUtcNow())) return Task.FromResult(0);
            var ids = _store.Where(pair => pair.Value.Data.Issuer == logout.Issuer &&
                (logout.Subject is null || pair.Value.Data.UserId == logout.Subject) &&
                (logout.SessionId is null || pair.Value.Data.ProviderSessionId == logout.SessionId)).Select(pair => pair.Key).ToArray();
            foreach (var id in ids) Remove(id);
            return Task.FromResult(ids.Length);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryUseNonceAsync(string purpose, string nonce, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose); ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        lock (_gate) return Task.FromResult(UseNonce(purpose, nonce, ttl));
    }

    private bool UseNonce(string purpose, string nonce, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero) return false;
        var now = _timeProvider.GetUtcNow();
        foreach (var expired in _nonces.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray()) _nonces.Remove(expired);
        var key = (purpose, nonce);
        if (_nonces.ContainsKey(key)) return false;
        _nonces[key] = now + ttl;
        return true;
    }

    private void Remove(string id)
    {
        if (!_store.Remove(id, out var entry)) return;
        if (_users.TryGetValue(entry.Data.UserId, out var ids)) { ids.Remove(id); if (ids.Count == 0) _users.Remove(entry.Data.UserId); }
    }
    private sealed record Entry(SessionData Data, DateTimeOffset ExpiresAt);
}
