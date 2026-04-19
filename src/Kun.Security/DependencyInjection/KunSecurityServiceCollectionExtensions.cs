using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.Security.MultiTenancy;
using Kun.Security.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Security.DependencyInjection;

/// <summary>
/// Extension methods for registering Kun Security services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class KunSecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers Kun Security services:
    /// <list type="bullet">
    ///   <item><see cref="IHttpContextAccessor"/> (if not already registered)</item>
    ///   <item>Scoped <see cref="ITenantContext"/> → <see cref="JwtTenantContext"/></item>
    ///   <item>Scoped <see cref="ICurrentUser"/> → <see cref="JwtCurrentUser"/></item>
    /// </list>
    /// Call <c>AddAuthentication().AddKunServiceAuthentication(...)</c> separately to configure
    /// JWT Bearer validation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddKunSecurity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.TryAddScoped<ITenantContext, JwtTenantContext>();
        services.TryAddScoped<ICurrentUser, JwtCurrentUser>();

        return services;
    }
}
