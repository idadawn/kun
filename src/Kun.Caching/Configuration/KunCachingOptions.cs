namespace Kun.Caching.Configuration;

/// <summary>Configuration for <c>AddKunCaching</c>.</summary>
public sealed class KunCachingOptions
{
    /// <summary>
    /// Prefix prepended to every key passed to the cache. Ideal for
    /// namespacing per-service caches on a shared Redis. Empty / null
    /// means no prefix (default).
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// TTL applied to <c>SetAsync</c> / <c>GetOrSetAsync</c> calls that
    /// omit an explicit <c>ttl</c>. <see langword="null"/> (default)
    /// means entries never expire when no explicit TTL is given.
    /// </summary>
    public TimeSpan? DefaultTtl { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the Redis backend swallows
    /// connection / socket exceptions and routes reads and writes to a
    /// sidecar in-memory cache for the duration of the outage. The
    /// exception is logged at <c>Warning</c> and never propagated.
    /// Default is <see langword="false"/>; callers see the exception
    /// and decide.
    /// </summary>
    public bool FallbackToMemoryOnRedisFailure { get; set; }
}
