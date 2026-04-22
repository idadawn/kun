using Kun.Identity.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kun.Identity.DependencyInjection;

/// <summary>Service-collection extensions that register Kun.Identity.</summary>
public static class KunIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Kun.Identity facade. Configure via the fluent
    /// <see cref="KunIdentityBuilder"/>: call <c>AddJwtBearer</c>,
    /// <c>AddS2SIssuer</c>, and/or <c>AddOidcClient</c> on the builder.
    /// Calling <c>AddKunIdentity</c> on its own is a no-op — it only
    /// wires the builder surface.
    /// </summary>
    public static IServiceCollection AddKunIdentity(
        this IServiceCollection services,
        Action<KunIdentityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new KunIdentityBuilder(services);
        configure(builder);
        return services;
    }
}
