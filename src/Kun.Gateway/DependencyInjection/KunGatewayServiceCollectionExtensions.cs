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
    /// <param name="reverseProxyConfig">
    /// Optional YARP configuration section (e.g. <c>Configuration.GetSection("ReverseProxy")</c>)
    /// to bind Routes/Clusters. When null, callers must register their own
    /// <see cref="Yarp.ReverseProxy.Configuration.IProxyConfigProvider"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunGateway(
        this IServiceCollection services,
        Action<KunGatewayOptions> configure,
        Microsoft.Extensions.Configuration.IConfiguration? reverseProxyConfig = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<KunGatewayOptions>()
            .Configure(configure);

        services.TryAddSingleton<S2sTokenIssuer>();

        // Default partition resolver — tenant-based; can be overridden before calling AddKunGateway
        services.TryAddSingleton<RateLimiting.IRateLimitPartitionResolver, TenantRateLimitPartitionResolver>();

        // AddReverseProxy() is NOT idempotent — calling it twice re-registers internal YARP
        // services (IPassiveHealthCheckPolicy etc.) and MapReverseProxy throws. Do the whole
        // proxy wiring (transforms + config source) in a single chain.
        var proxyBuilder = services
            .AddReverseProxy()
            .AddTransforms(ctx =>
            {
                var issuer = ctx.Services.GetRequiredService<S2sTokenIssuer>();
                ctx.RequestTransforms.Add(new S2sAuthTransform(issuer));
            });

        if (reverseProxyConfig is not null)
        {
            proxyBuilder.LoadFromConfig(reverseProxyConfig);
        }

        return services;
    }
}
