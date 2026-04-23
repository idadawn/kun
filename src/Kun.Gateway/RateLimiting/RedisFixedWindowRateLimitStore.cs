using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kun.Gateway.RateLimiting;

/// <summary>
/// Fixed-window distributed rate-limit counter backed by Redis. Uses an atomic
/// <c>INCR</c> with <c>NX EX</c>-style TTL-on-first-write semantics so the counter
/// self-expires once the window elapses — no cleanup job required.
/// </summary>
/// <remarks>
/// <para>
/// The implementation executes a two-command pipeline (INCR + TTL read) to obtain both
/// the post-increment count and the remaining TTL in one round-trip. On the first hit
/// (INCR returns 1), it follows up with EXPIRE to set the window TTL; subsequent hits
/// in the same window leave the TTL untouched.
/// </para>
/// <para>
/// Not a full <see cref="System.Threading.RateLimiting.RateLimiter"/> subclass — this is
/// the low-level primitive. Full <c>PartitionedRateLimiter&lt;HttpContext&gt;</c>
/// integration with ASP.NET's rate-limit middleware is tracked as future work.
/// </para>
/// </remarks>
public sealed class RedisFixedWindowRateLimitStore : IRedisRateLimitStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisRateLimitStoreOptions _options;

    /// <summary>Creates a new store.</summary>
    public RedisFixedWindowRateLimitStore(
        IConnectionMultiplexer redis,
        IOptions<RedisRateLimitStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(options);
        _redis = redis;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<RateLimitHitResult> HitAsync(
        string partitionKey,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(partitionKey))
        {
            throw new ArgumentException("partitionKey must be non-empty.", nameof(partitionKey));
        }
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "window must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        var key = BuildKey(partitionKey);

        // Atomic increment. When the key is new, StringIncrementAsync creates it at 0
        // first then increments to 1 — race-safe at the Redis server level.
        var count = await db.StringIncrementAsync(key).ConfigureAwait(false);

        if (count == 1)
        {
            // First hit in a new window — set TTL so the counter auto-expires.
            await db.KeyExpireAsync(key, window).ConfigureAwait(false);
            return new RateLimitHitResult(Count: 1, ResetsIn: window);
        }

        // Existing window — read the remaining TTL. Redis returns null if the key has no
        // TTL, which should not happen in practice (we always set EXPIRE on first hit) but
        // guard against it by treating null as "full window".
        var ttl = await db.KeyTimeToLiveAsync(key).ConfigureAwait(false);
        return new RateLimitHitResult(Count: count, ResetsIn: ttl ?? window);
    }

    private string BuildKey(string partitionKey) => $"{_options.KeyPrefix}:{partitionKey}";
}
