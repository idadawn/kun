using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Kun.Gateway.RateLimiting;

/// <summary>
/// Sliding-window rate-limiter policy partitioned by
/// <see cref="IRateLimitPartitionResolver"/>. Options bound via
/// <see cref="KunSlidingWindowOptions"/>.
/// </summary>
internal sealed class KunSlidingWindowRateLimiterPolicy : IRateLimiterPolicy<string>
{
    private readonly IRateLimitPartitionResolver _resolver;
    private readonly KunSlidingWindowOptions _options;

    public KunSlidingWindowRateLimiterPolicy(
        IRateLimitPartitionResolver resolver,
        IOptions<KunSlidingWindowOptions> options)
    {
        _resolver = resolver;
        _options = options.Value;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var key = _resolver.ResolvePartitionKey(httpContext);
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = _options.PermitLimit,
            Window = _options.Window,
            SegmentsPerWindow = _options.SegmentsPerWindow,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = _options.QueueLimit,
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => KunRateLimiterExtensions.Reject;
}
