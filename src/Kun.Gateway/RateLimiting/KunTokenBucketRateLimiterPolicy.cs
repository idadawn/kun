using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Kun.Gateway.RateLimiting;

/// <summary>
/// Token-bucket rate-limiter policy partitioned by
/// <see cref="IRateLimitPartitionResolver"/>. Options bound via
/// <see cref="KunTokenBucketOptions"/>.
/// </summary>
internal sealed class KunTokenBucketRateLimiterPolicy : IRateLimiterPolicy<string>
{
    private readonly IRateLimitPartitionResolver _resolver;
    private readonly KunTokenBucketOptions _options;

    public KunTokenBucketRateLimiterPolicy(
        IRateLimitPartitionResolver resolver,
        IOptions<KunTokenBucketOptions> options)
    {
        _resolver = resolver;
        _options = options.Value;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var key = _resolver.ResolvePartitionKey(httpContext);
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = _options.TokenLimit,
            TokensPerPeriod = _options.TokensPerPeriod,
            ReplenishmentPeriod = _options.ReplenishmentPeriod,
            AutoReplenishment = _options.AutoReplenishment,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = _options.QueueLimit,
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => KunRateLimiterExtensions.Reject;
}
