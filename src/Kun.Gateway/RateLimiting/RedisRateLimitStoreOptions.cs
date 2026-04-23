namespace Kun.Gateway.RateLimiting;

/// <summary>Configuration for <see cref="RedisFixedWindowRateLimitStore"/>.</summary>
public sealed class RedisRateLimitStoreOptions
{
    /// <summary>Config section name for binding via <c>Configure&lt;...&gt;</c>.</summary>
    public const string SectionName = "Kun:Gateway:RateLimit:Redis";

    /// <summary>
    /// Redis key prefix. Keys take the form <c>{KeyPrefix}:{partitionKey}</c>. Change this
    /// when the same Redis instance is shared across multiple Kun gateways that must not
    /// collide (e.g. staging vs prod, tenant-A vs tenant-B clusters). Default <c>"kun:rl"</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "kun:rl";
}
