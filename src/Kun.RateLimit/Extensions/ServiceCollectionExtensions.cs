using System.Globalization;
using System.Threading.RateLimiting;
using Kun.RateLimit.Abstractions;
using Kun.RateLimit.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.RateLimit.Extensions;

/// <summary>Service-collection extensions that register unified Kun rate limiting.</summary>
public static class KunRateLimitServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RateLimiterOptions"/> built from the supplied
    /// <see cref="KunRateLimitOptions"/>. Each named window becomes a
    /// fixed-window partition policy buckets on the configured
    /// <see cref="RateLimitPartitionKey"/>. Rejected requests receive a
    /// <see cref="StatusCodes.Status429TooManyRequests"/> response with a
    /// <c>Retry-After</c> header; the optional
    /// <see cref="KunRateLimitOptions.OnRejected"/> hook runs after the
    /// default response is written.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Options callback; must register at least one window.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddKunRateLimit(
        this IServiceCollection services,
        Action<KunRateLimitOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new KunRateLimitOptions();
        configure(options);

        services.AddRateLimiter(rl =>
        {
            foreach (var (name, rule) in options.Windows)
            {
                var captured = rule;
                rl.AddPolicy(name, httpContext =>
                {
                    var partitionKey = PartitionKeyResolver.Resolve(captured.PartitionKey, httpContext);
                    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = captured.PermitLimit,
                        Window = captured.Period,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = captured.QueueLimit,
                        AutoReplenishment = captured.AutoReplenishment,
                    });
                });
            }

            rl.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var retryAfterSeconds = ResolveRetryAfter(context, options);
                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                if (options.OnRejected is { } hook)
                {
                    await hook(context, cancellationToken).ConfigureAwait(false);
                }
            };
        });

        return services;
    }

    private static int ResolveRetryAfter(OnRejectedContext context, KunRateLimitOptions options)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            return Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        // When the lease does not carry RetryAfter metadata, fall back to the
        // largest registered window so callers still receive a usable hint.
        var fallback = options.Windows.Values
            .Select(w => w.Period)
            .DefaultIfEmpty(TimeSpan.FromMinutes(1))
            .Max();
        return Math.Max(1, (int)Math.Ceiling(fallback.TotalSeconds));
    }
}
