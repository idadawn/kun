using Kun.Identity.Abstractions.Session;
using Kun.Identity.Implementations.Session;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kun.Identity.Configuration;

/// <summary>
/// Configuration surface exposed by
/// <c>KunIdentityBuilder.AddCookieSession(opts =&gt; ...)</c>. Inherits
/// every <see cref="KunCookieAuthOptions"/> field (so callers write
/// <c>opts.CookieName = "kun.sid"; opts.SessionTtl = ...;</c> as
/// expected) and adds the backend-selection methods
/// <see cref="UseInMemoryStore"/> and <see cref="UseRedisStore()"/>.
/// Exactly one backend call should be made — multiple calls take
/// last-wins semantics because each call replaces the
/// <see cref="ISessionStore"/> registration on the underlying service
/// collection.
/// </summary>
public sealed class KunCookieSessionBuilder : KunCookieAuthOptions
{
    private readonly IServiceCollection _services;

    /// <summary>Creates the builder bound to <paramref name="services"/>.</summary>
    public KunCookieSessionBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>
    /// Registers <see cref="InMemorySessionStore"/> as the backend.
    /// Suitable for integration tests and single-process development.
    /// Adds a <see cref="TimeProvider"/> if none is already registered.
    /// </summary>
    public KunCookieSessionBuilder UseInMemoryStore()
    {
        _services.TryAddSingleton(TimeProvider.System);
        _services.AddSingleton<ISessionStore>(sp =>
            new InMemorySessionStore(sp.GetRequiredService<TimeProvider>()));
        return this;
    }

    /// <summary>
    /// Registers <see cref="RedisSessionStore"/> as the backend. The
    /// caller registers IConnectionMultiplexer, or configures Kun.Caching UseRedis.
    /// A direct Redis connection is required for atomic indexes and revocation; no memory fallback is used.
    /// </summary>
    public KunCookieSessionBuilder UseRedisStore() => UseRedisStore("kun:identity:");

    /// <summary>Registers the atomic Redis backend under an explicitly isolated application prefix.</summary>
    public KunCookieSessionBuilder UseRedisStore(string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        _services.TryAddSingleton(TimeProvider.System);
        _services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
            var configuration = options.ConfigurationOptions ?? (string.IsNullOrWhiteSpace(options.Configuration)
                ? throw new InvalidOperationException("Configure Redis or register IConnectionMultiplexer before UseRedisStore.")
                : ConfigurationOptions.Parse(options.Configuration));
            return ConnectionMultiplexer.Connect(configuration);
        });
        _services.AddSingleton<ISessionStore>(sp => new RedisSessionStore(sp.GetRequiredService<IConnectionMultiplexer>(), sp.GetRequiredService<TimeProvider>(), keyPrefix));
        return this;
    }
}
