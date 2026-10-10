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
    [Obsolete("Use Kun.Gateway.S2sTokenIssuer.", DiagnosticId = "KUNS2S001")]
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
        Services.TryAddSingleton(TimeProvider.System);

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
            return new KunOidcClient(options, factory, sp.GetRequiredService<TimeProvider>());
        });
        return this;
    }

    /// <summary>
    /// Registers the cookie-session pipeline described in
    /// <c>ADR-010</c>: a <c>KunCookieAuthOptions</c> options entry, a
    /// default <see cref="TimeProvider"/>, and the
    /// <c>ISessionStore</c> backend the caller selects via
    /// <c>opts.UseInMemoryStore()</c> or <c>opts.UseRedisStore()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>UseKunCookieAuth()</c> middleware,
    /// <c>SignInHelper</c>, <c>ChallengeHelper</c>, and
    /// <c>KunCurrentUser</c> DI entries live in the companion package
    /// <c>Kun.Identity.AspNetCore</c>; callers wiring a browser-flow
    /// service must reference that package in addition to calling
    /// <see cref="AddCookieSession"/>.
    /// </para>
    /// <para>
    /// If neither <c>UseInMemoryStore</c> nor <c>UseRedisStore</c> is
    /// called within the lambda, resolving <c>ISessionStore</c> from
    /// DI will throw — the registration is intentionally not defaulted
    /// because an unintended in-memory store in production would be a
    /// subtle data-loss / cross-replica-drift bug.
    /// </para>
    /// </remarks>
    public KunIdentityBuilder AddCookieSession(Action<KunCookieSessionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new KunCookieSessionBuilder(Services);
        configure(builder);

        // Mirror the builder's options into the DI options entry.
        Services.Configure<KunCookieAuthOptions>(opts =>
        {
            opts.CookieName = builder.CookieName;
            opts.Domain = builder.Domain;
            opts.Path = builder.Path;
            opts.HttpOnly = builder.HttpOnly;
            opts.SameSite = builder.SameSite;
            opts.Secure = builder.Secure;
            opts.SessionTtl = builder.SessionTtl;
            opts.SlidingExpiration = builder.SlidingExpiration;
            opts.RequireAntiforgery = builder.RequireAntiforgery;
        });
        Services.TryAddSingleton(TimeProvider.System);
        return this;
    }

    internal static string OidcHttpClientName(string name) => $"Kun.Identity.Oidc:{name}";
}
