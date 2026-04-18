using StackExchange.Redis;

namespace Kun.EventBus.Background;

/// <summary>
/// Redis-backed <see cref="IOutboxLockProvider"/> using <c>SET key value NX EX</c> semantics.
/// Safe for multi-pod deployments: only one pod holds the lock per TTL window.
/// </summary>
public sealed class RedisOutboxLockProvider : IOutboxLockProvider
{
    private readonly IDatabase _db;

    /// <summary>Initializes the provider with an explicit Redis <paramref name="database"/>.</summary>
    public RedisOutboxLockProvider(IDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _db = database;
    }

    /// <summary>
    /// Initializes the provider by resolving the default database from
    /// <paramref name="multiplexer"/>.
    /// </summary>
    public RedisOutboxLockProvider(IConnectionMultiplexer multiplexer)
        : this(multiplexer.GetDatabase()) { }

    /// <inheritdoc/>
    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string lockName,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(lockName);

        // A nonce ensures only the holder can release the lock.
        var nonce = Guid.NewGuid().ToString("N");
        var key = (RedisKey)$"outbox-lock:{lockName}";

        bool acquired = await _db.StringSetAsync(key, nonce, ttl, When.NotExists).ConfigureAwait(false);
        if (!acquired)
        {
            return null;
        }

        return new RedisLease(_db, key, nonce);
    }

    private sealed class RedisLease : IAsyncDisposable
    {
        // Lua script: delete key only if our nonce still matches (atomic compare-and-delete).
        private const string ReleaseScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

        private readonly IDatabase _db;
        private readonly RedisKey _key;
        private readonly string _nonce;
        private bool _disposed;

        internal RedisLease(IDatabase db, RedisKey key, string nonce)
        {
            _db = db;
            _key = key;
            _nonce = nonce;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await _db.ScriptEvaluateAsync(
                ReleaseScript,
                keys: [_key],
                values: [(RedisValue)_nonce]).ConfigureAwait(false);
        }
    }
}
