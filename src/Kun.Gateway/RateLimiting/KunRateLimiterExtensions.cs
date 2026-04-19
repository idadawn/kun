using System.Threading.RateLimiting;
using Kun.Gateway.RateLimiting.Resolvers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Gateway.RateLimiting;

/// <summary>
/// Extension methods for registering Kun rate limiting services.
/// </summary>
public static class KunRateLimiterExtensions
{
    internal const string PolicyName = "kun-default";
    internal const int DefaultPermitLimit = 100;

    /// <summary>
    /// Registers Kun rate limiting with a tenant-partitioned fixed-window policy (100 req/min by default).
    /// Requires an <see cref="IRateLimitPartitionResolver"/> to be registered; if none is present,
    /// defaults to <see cref="TenantRateLimitPartitionResolver"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to further configure rate limiter options.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunRateLimiter(
        this IServiceCollection services,
        Action<RateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register a default resolver if none has been registered
        services.TryAddSingleton<IRateLimitPartitionResolver, TenantRateLimitPartitionResolver>();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy<string, KunRateLimiterPolicy>(PolicyName);
            configure?.Invoke(options);
        });

        return services;
    }
}

/// <summary>
/// Fixed-window rate limiter policy that partitions by the key resolved from <see cref="IRateLimitPartitionResolver"/>.
/// </summary>
internal sealed class KunRateLimiterPolicy : IRateLimiterPolicy<string>
{
    private readonly IRateLimitPartitionResolver _resolver;

    public KunRateLimiterPolicy(IRateLimitPartitionResolver resolver)
    {
        _resolver = resolver;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var key = _resolver.ResolvePartitionKey(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = KunRateLimiterExtensions.DefaultPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected =>
        static (ctx, _) =>
        {
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return ValueTask.CompletedTask;
        };
}
