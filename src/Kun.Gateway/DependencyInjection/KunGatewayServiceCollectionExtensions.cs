using Kun.Gateway.Configuration;
using Kun.Gateway.RateLimiting.Resolvers;
using Kun.Gateway.Transforms;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Gateway.DependencyInjection;

/// <summary>
/// Extension methods for registering Kun Gateway services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class KunGatewayServiceCollectionExtensions
{
    /// <summary>
    /// Registers core Kun Gateway services:
    /// <list type="bullet">
    ///   <item><see cref="KunGatewayOptions"/> bound from the provided delegate.</item>
    ///   <item><see cref="S2sTokenIssuer"/> as a singleton.</item>
    ///   <item>Default <see cref="TenantRateLimitPartitionResolver"/> (override by registering your own before calling this).</item>
    ///   <item>YARP reverse proxy with the <see cref="S2sAuthTransform"/> applied to all routes.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Delegate to configure <see cref="KunGatewayOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunGateway(
        this IServiceCollection services,
        Action<KunGatewayOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<KunGatewayOptions>()
            .Configure(configure);

        services.TryAddSingleton<S2sTokenIssuer>();

        // Default partition resolver — tenant-based; can be overridden before calling AddKunGateway
        services.TryAddSingleton<RateLimiting.IRateLimitPartitionResolver, TenantRateLimitPartitionResolver>();

        services
            .AddReverseProxy()
            .AddTransforms(ctx =>
            {
                var issuer = ctx.Services.GetRequiredService<S2sTokenIssuer>();
                ctx.RequestTransforms.Add(new S2sAuthTransform(issuer));
            });

        return services;
    }
}
