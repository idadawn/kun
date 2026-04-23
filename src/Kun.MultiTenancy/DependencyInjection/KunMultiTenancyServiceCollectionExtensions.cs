using Kun.Core.MultiTenancy;
using Kun.MultiTenancy.Abstractions;
using Kun.MultiTenancy.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.MultiTenancy.DependencyInjection;

/// <summary>Service-collection extensions that wire up Kun multi-tenancy.</summary>
public static class KunMultiTenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ITenantContext"/> as an
    /// <see cref="AsyncLocalTenantContext"/> singleton,
    /// <see cref="ITenantResolver"/> as
    /// <see cref="DefaultTenantResolver"/>, and the
    /// <see cref="KunMultiTenancyOptions"/> configured by
    /// <paramref name="configure"/>. Pair with
    /// <c>app.UseKunMultiTenancy()</c> to scope the AsyncLocal context
    /// for each request.
    /// </summary>
    public static IServiceCollection AddKunMultiTenancy(
        this IServiceCollection services,
        Action<KunMultiTenancyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton<AsyncLocalTenantContext>();
        services.RemoveAll<ITenantContext>();
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AsyncLocalTenantContext>());

        services.RemoveAll<ITenantResolver>();
        services.AddSingleton<ITenantResolver, DefaultTenantResolver>();

        return services;
    }
}
