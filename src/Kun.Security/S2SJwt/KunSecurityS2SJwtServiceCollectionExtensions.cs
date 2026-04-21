using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Security.S2SJwt;

/// <summary>
/// Extension methods for registering Kun S2S JWT issuer and verifier services.
/// </summary>
public static class KunSecurityS2SJwtServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IS2SJwtIssuer"/> and <see cref="IS2SJwtVerifier"/>
    /// configured with ES256 signing keys from <see cref="S2SJwtOptions"/>.
    /// </summary>
    public static IServiceCollection AddS2SJwt(
        this IServiceCollection services,
        Action<S2SJwtOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton<IS2SJwtIssuer, S2SJwtIssuer>();
        services.TryAddSingleton<IS2SJwtVerifier, S2SJwtVerifier>();

        return services;
    }
}
