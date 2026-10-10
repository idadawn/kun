using System.Net;
using Kun.Gateway.Configuration;
using Kun.Gateway.Identity;
using Kun.Gateway.Jwks;
using Kun.Gateway.RateLimiting;
using Kun.Gateway.RateLimiting.Resolvers;
using Kun.Gateway.Transforms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

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
    /// When <paramref name="reverseProxyConfig"/> is <see langword="null"/>, a startup validation
    /// service checks that an <see cref="IProxyConfigProvider"/> has been registered and throws a
    /// descriptive <see cref="InvalidOperationException"/> on host start if one is missing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Delegate to configure <see cref="KunGatewayOptions"/>.</param>
    /// <param name="reverseProxyConfig">
    /// Optional YARP configuration section (e.g. <c>Configuration.GetSection("ReverseProxy")</c>)
    /// to bind Routes/Clusters. When null, callers must register their own
    /// <see cref="IProxyConfigProvider"/>.
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

        services.AddHttpContextAccessor();
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddKunRateLimiter();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<S2sSigningKeyRing>();
        services.AddHealthChecks().AddCheck<GatewaySigningHealthCheck>("gateway-signing", tags: ["ready"]);
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<KunGatewayOptions>>((forwarded, gateway) =>
        {
            var options = gateway.Value;
            if (options.ForwardLimit <= 0) throw new InvalidOperationException("ForwardLimit must be positive.");
            forwarded.ForwardLimit = options.ForwardLimit;
            forwarded.KnownProxies.Clear(); forwarded.KnownIPNetworks.Clear();
            foreach (var address in options.TrustedProxyAddresses) forwarded.KnownProxies.Add(IPAddress.Parse(address));
            foreach (var network in options.TrustedProxyNetworks) forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            // 空白名单时关闭处理，不能把清空 KnownProxies 误当成默认拒绝（ASP.NET 会视为信任全部）。
            forwarded.ForwardedHeaders = options.TrustedProxyAddresses.Length + options.TrustedProxyNetworks.Length > 0
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto : ForwardedHeaders.None;
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProxyConfigFilter, GatewayRouteDefaults>());

        // Default no-op resolver; consumers wanting enrichment register their own
        // IS2sPrincipalResolver before AddKunGateway runs (TryAddSingleton below is a no-op
        // when the slot is already filled).
        services.TryAddSingleton<IS2sPrincipalResolver, NoOpS2sPrincipalResolver>();

        // Retain the public-file builder for consumers that map JWKS without AddKunGateway.
        // Gateway endpoints use the shared signing ring, including its overlap keys.
        services.TryAddSingleton<JwksResponseBuilder>();

        services.TryAddSingleton(provider => new S2sTokenIssuer(provider.GetRequiredService<IOptions<KunGatewayOptions>>(), provider.GetRequiredService<IHostEnvironment>(), provider.GetRequiredService<IS2sPrincipalResolver>(), provider.GetService<IHttpContextAccessor>(), provider.GetRequiredService<S2sSigningKeyRing>()));

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
        else
        {
            // When no config section is provided, register a startup check so a missing
            // IProxyConfigProvider is caught at host start rather than at the first request.
            services.AddHostedService<ReverseProxyConfigValidationService>();
        }

        return services;
    }
}

/// <summary>
/// Hosted service that asserts an <see cref="IProxyConfigProvider"/> has been registered
/// when <see cref="KunGatewayServiceCollectionExtensions.AddKunGateway"/> was called without
/// a <c>reverseProxyConfig</c> section. Fails fast at host startup with a user-friendly message.
/// </summary>
internal sealed class ReverseProxyConfigValidationService : IHostedService
{
    private readonly IServiceProvider _services;

    /// <summary>Initializes the validation service.</summary>
    public ReverseProxyConfigValidationService(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var provider = _services.GetService<IProxyConfigProvider>();
        if (provider is null)
        {
            throw new InvalidOperationException(
                "Kun.Gateway was registered without a ReverseProxy config source. " +
                "Call AddKunGateway(configure, Configuration.GetSection(\"ReverseProxy\")) OR register an " +
                "IProxyConfigProvider yourself before MapReverseProxy.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>仅为未声明策略的业务路由补默认值；显式 Anonymous 仍是唯一匿名白名单。</summary>
internal sealed class GatewayRouteDefaults : IProxyConfigFilter
{
    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel) => ValueTask.FromResult(cluster);
    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
        => ValueTask.FromResult(route with
        {
            AuthorizationPolicy = route.AuthorizationPolicy ?? "Default",
            RateLimiterPolicy = route.RateLimiterPolicy ?? KunRateLimiterExtensions.PolicyName
        });
}
