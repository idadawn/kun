using StackExchange.Redis;

namespace Kun.EventBus.Idempotency;

/// <summary>
/// Redis-backed implementation of <see cref="IProcessedMessageStore"/> using
/// <c>SET … NX EX</c> for atomic first-write semantics. Suitable for distributed,
/// multi-instance deployments.
/// </summary>
public sealed class RedisProcessedMessageStore : IProcessedMessageStore
{
    private readonly IDatabase _db;

    /// <summary>Initialises the store with the given Redis <paramref name="database"/>.</summary>
    public RedisProcessedMessageStore(IDatabase database) => _db = database;

    /// <summary>
    /// Initialises the store by resolving the default database from
    /// <paramref name="multiplexer"/>.
    /// </summary>
    public RedisProcessedMessageStore(IConnectionMultiplexer multiplexer)
        : this(multiplexer.GetDatabase()) { }

    /// <inheritdoc/>
    public async ValueTask<bool> TryMarkProcessedAsync(
        string consumerName,
        Guid messageId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        // Key format: idemp:{consumerName}:{messageId}
        var key = (RedisKey)$"idemp:{consumerName}:{messageId}";

        // SET key "1" NX EX <seconds> — returns true only when the key was absent.
        bool set = await _db.StringSetAsync(key, "1", ttl, When.NotExists).ConfigureAwait(false);
        return set;
    }
}
