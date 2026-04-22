using Kun.Gateway.Configuration;
using Kun.Gateway.Identity;
using Kun.Gateway.RateLimiting.Resolvers;
using Kun.Gateway.Transforms;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Default no-op resolver; consumers wanting enrichment register their own
        // IS2sPrincipalResolver before AddKunGateway runs (TryAddSingleton below is a no-op
        // when the slot is already filled).
        services.TryAddSingleton<IS2sPrincipalResolver, NoOpS2sPrincipalResolver>();

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
