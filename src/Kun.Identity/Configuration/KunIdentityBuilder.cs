using Kun.Identity.Abstractions;
using Kun.Identity.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Identity.Configuration;

/// <summary>Fluent builder surfaced by <c>AddKunIdentity</c>.</summary>
public sealed class KunIdentityBuilder
{
    /// <summary>Creates a builder bound to <paramref name="services"/>.</summary>
    public KunIdentityBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers <see cref="IJwtValidator"/> configured for external
    /// Bearer JWTs. Subsequent calls replace the existing options so
    /// consumers can re-configure without double-registration.
    /// </summary>
    public KunIdentityBuilder AddJwtBearer(Action<KunJwtBearerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        Services.TryAddSingleton<IJwtValidator, KunJwtValidator>();
        return this;
    }

    /// <summary>Registers <see cref="IJwtIssuer"/> for internal S2S token minting.</summary>
    public KunIdentityBuilder AddS2SIssuer(Action<KunJwtIssuerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        Services.TryAddSingleton<IJwtIssuer, KunJwtIssuer>();
        return this;
    }

    /// <summary>
    /// Registers a named <see cref="IOidcClient"/>. Multiple calls with
    /// distinct <paramref name="name"/> values register multiple
    /// clients — resolve them from DI via the registered
    /// <c>IEnumerable&lt;IOidcClient&gt;</c> and match by
    /// <see cref="IOidcClient.Name"/>.
    /// </summary>
    public KunIdentityBuilder AddOidcClient(string name, Action<KunOidcClientOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddHttpClient(OidcHttpClientName(name));

        // Register the options twice: once as a named options entry so
        // downstream helpers (e.g. Kun.Identity.AspNetCore.ChallengeHelper)
        // can resolve the per-client configuration via IOptionsMonitor
        // without reflection, and once as the IOidcClient singleton used
        // for discovery + token exchange.
        Services.Configure<KunOidcClientOptions>(name, opts =>
        {
            configure(opts);
            opts.Name = name;
        });

        Services.AddSingleton<IOidcClient>(sp =>
        {
            var options = new KunOidcClientOptions { Name = name };
            configure(options);
            options.Name = name;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new KunOidcClient(options, factory);
        });
        return this;
    }

    internal static string OidcHttpClientName(string name) => $"Kun.Identity.Oidc:{name}";
}
