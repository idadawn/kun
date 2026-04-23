namespace Kun.Gateway.RateLimiting;

/// <summary>
/// Tunable parameters for <c>AddKunTokenBucketRateLimiter</c>. Token-bucket smooths bursty
/// traffic by accumulating tokens at a steady rate up to a cap; each request consumes one
/// token and is rejected (or queued) when the bucket is empty.
/// </summary>
public sealed class KunTokenBucketOptions
{
    /// <summary>Maximum tokens the bucket holds. Effectively the burst budget. Default 100.</summary>
    public int TokenLimit { get; set; } = 100;

    /// <summary>How many tokens refill each <see cref="ReplenishmentPeriod"/>. Default 20.</summary>
    public int TokensPerPeriod { get; set; } = 20;

    /// <summary>Interval between token refills. Default 10 seconds (so 20 tokens/10s = 120 tokens/minute).</summary>
    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Queue capacity for requests that arrive with an empty bucket. Default 0 (immediate rejection).</summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// When <see langword="true"/>, tokens refill automatically on a background timer.
    /// When <see langword="false"/>, the limiter only refills on incoming requests (cheaper
    /// but with coarser smoothing). Default <see langword="true"/>.
    /// </summary>
    public bool AutoReplenishment { get; set; } = true;
}
