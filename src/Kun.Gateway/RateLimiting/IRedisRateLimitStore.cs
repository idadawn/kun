namespace Kun.Gateway.RateLimiting;

/// <summary>
/// Low-level Redis-backed counter used for distributed rate limiting across multiple
/// Gateway pods. Primitive surface for a future <c>PartitionedRateLimiter&lt;HttpContext&gt;</c>
/// integration; directly usable today as a building block for custom rate-limit middleware.
/// </summary>
public interface IRedisRateLimitStore
{
    /// <summary>
    /// Atomically increments the count for <paramref name="partitionKey"/> within a
    /// rolling window of <paramref name="window"/>, applying TTL on the first write so
    /// stale partitions self-expire. Returns the post-increment count and the time until
    /// the window resets.
    /// </summary>
    /// <param name="partitionKey">Caller's partition key (tenant, IP, sub, etc.).</param>
    /// <param name="window">Window length; controls Redis EXPIRE TTL on first hit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>Count</c>: current number of hits within the active window (&gt;= 1).
    /// <c>ResetsIn</c>: time until the window resets. Never negative.
    /// </returns>
    Task<RateLimitHitResult> HitAsync(string partitionKey, TimeSpan window, CancellationToken cancellationToken = default);
}

/// <summary>Result of <see cref="IRedisRateLimitStore.HitAsync"/>.</summary>
/// <param name="Count">Post-increment hit count within the active window (&gt;= 1).</param>
/// <param name="ResetsIn">Time until the window resets and the counter is cleared.</param>
public readonly record struct RateLimitHitResult(long Count, TimeSpan ResetsIn);
