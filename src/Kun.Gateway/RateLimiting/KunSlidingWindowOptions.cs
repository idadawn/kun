namespace Kun.Gateway.RateLimiting;

/// <summary>
/// Tunable parameters for <c>AddKunSlidingWindowRateLimiter</c>. Sliding-window divides
/// each window into segments and shifts one segment at a time, yielding smoother behavior
/// than a fixed window at the cost of proportionally higher CPU/memory.
/// </summary>
public sealed class KunSlidingWindowOptions
{
    /// <summary>Maximum permitted requests per sliding window. Default 100.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Total window duration. Default 1 minute.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Number of segments in each window. More segments = smoother enforcement but higher
    /// memory per partition. Default 6 (10-second segments for a 1-minute window).
    /// </summary>
    public int SegmentsPerWindow { get; set; } = 6;

    /// <summary>Queue capacity for requests that exceed the permit limit. Default 0 (immediate rejection).</summary>
    public int QueueLimit { get; set; }
}
