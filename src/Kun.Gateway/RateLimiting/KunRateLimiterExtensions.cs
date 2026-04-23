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

    /// <summary>Name of the named policy registered by <c>AddKunSlidingWindowRateLimiter</c>.</summary>
    public const string SlidingWindowPolicyName = "kun-sliding-window";

    /// <summary>Name of the named policy registered by <c>AddKunTokenBucketRateLimiter</c>.</summary>
    public const string TokenBucketPolicyName = "kun-token-bucket";

    /// <summary>
    /// Registers a sliding-window rate limiter policy partitioned by the resolved
    /// <see cref="IRateLimitPartitionResolver"/> key. Call
    /// <c>app.MapXxx().RequireRateLimiting(KunRateLimiterExtensions.SlidingWindowPolicyName)</c>
    /// on endpoints that should enforce it. Defaults: 100 permits per 1-minute window with
    /// 6 segments. Defence-in-depth sibling to the fixed-window path; both may coexist.
    /// </summary>
    public static IServiceCollection AddKunSlidingWindowRateLimiter(
        this IServiceCollection services,
        Action<KunSlidingWindowOptions>? configureKun = null,
        Action<RateLimiterOptions>? configureHost = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRateLimitPartitionResolver, TenantRateLimitPartitionResolver>();

        if (configureKun is not null)
        {
            services.Configure(configureKun);
        }
        else
        {
            services.AddOptions<KunSlidingWindowOptions>();
        }

        services.AddRateLimiter(options =>
        {
            options.AddPolicy<string, KunSlidingWindowRateLimiterPolicy>(SlidingWindowPolicyName);
            configureHost?.Invoke(options);
        });

        return services;
    }

    /// <summary>
    /// Registers a token-bucket rate limiter policy partitioned by the resolved
    /// <see cref="IRateLimitPartitionResolver"/> key. Defaults: 100-token burst budget,
    /// refilled at 20 tokens / 10 seconds (≈120 tokens/minute steady state).
    /// Absorbs bursty traffic better than fixed-window; use for APIs where clients
    /// naturally come in bursts (webhook fanout, batch imports).
    /// </summary>
    /// <summary>
    /// Registers <see cref="RedisFixedWindowRateLimitStore"/> as the default
    /// <see cref="IRedisRateLimitStore"/> and binds <see cref="RedisRateLimitStoreOptions"/>.
    /// Consumers are responsible for registering their own <c>IConnectionMultiplexer</c>
    /// (e.g. via <c>AddStackExchangeRedisCache</c> or a direct Singleton registration).
    /// This primitive provides the distributed counter surface; full
    /// <c>PartitionedRateLimiter&lt;HttpContext&gt;</c> integration with ASP.NET's
    /// rate-limit middleware is tracked as future work.
    /// </summary>
    public static IServiceCollection AddKunRedisRateLimitStore(
        this IServiceCollection services,
        Action<RedisRateLimitStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RedisRateLimitStoreOptions>();
        }

        services.TryAddSingleton<IRedisRateLimitStore, RedisFixedWindowRateLimitStore>();
        return services;
    }

    /// <summary>
    /// Registers a token-bucket rate limiter policy partitioned by the resolved
    /// <see cref="IRateLimitPartitionResolver"/> key. Defaults: 100-token burst budget,
    /// refilled at 20 tokens / 10 seconds (≈120 tokens/minute steady state).
    /// Absorbs bursty traffic better than fixed-window; use for APIs where clients
    /// naturally come in bursts (webhook fanout, batch imports).
    /// </summary>
    public static IServiceCollection AddKunTokenBucketRateLimiter(
        this IServiceCollection services,
        Action<KunTokenBucketOptions>? configureKun = null,
        Action<RateLimiterOptions>? configureHost = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRateLimitPartitionResolver, TenantRateLimitPartitionResolver>();

        if (configureKun is not null)
        {
            services.Configure(configureKun);
        }
        else
        {
            services.AddOptions<KunTokenBucketOptions>();
        }

        services.AddRateLimiter(options =>
        {
            options.AddPolicy<string, KunTokenBucketRateLimiterPolicy>(TokenBucketPolicyName);
            configureHost?.Invoke(options);
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
