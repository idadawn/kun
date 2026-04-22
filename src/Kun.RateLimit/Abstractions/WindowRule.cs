namespace Kun.RateLimit.Abstractions;

/// <summary>Configuration for a single named rate-limit window.</summary>
public sealed class WindowRule
{
    /// <summary>Duration of each fixed window (default 1 minute).</summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Requests permitted per partition per window. Must be &gt; 0 for the window to have effect.</summary>
    public int PermitLimit { get; set; }

    /// <summary>Number of requests that can queue when the permit pool is exhausted (default 0 — reject immediately).</summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), the limiter auto-replenishes on
    /// a timer; when <see langword="false"/>, the caller is responsible for
    /// driving replenishment. Leave on for HTTP traffic.
    /// </summary>
    public bool AutoReplenishment { get; set; } = true;

    /// <summary>Dimension the limiter partitions on (tenant / user / IP).</summary>
    public RateLimitPartitionKey PartitionKey { get; set; } = RateLimitPartitionKey.Tenant;
}
