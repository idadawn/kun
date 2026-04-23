namespace Kun.RateLimit.Abstractions;

/// <summary>Identity dimension the rate limiter partitions on.</summary>
public enum RateLimitPartitionKey
{
    /// <summary>Partition by <c>tenant_id</c> claim of the authenticated principal; falls back to <c>"anonymous"</c>.</summary>
    Tenant,

    /// <summary>Partition by <c>sub</c> / <c>NameIdentifier</c> claim of the authenticated principal; falls back to <c>"anonymous"</c>.</summary>
    User,

    /// <summary>Partition by remote IP address, honouring <c>X-Forwarded-For</c> when present.</summary>
    Ip,
}
