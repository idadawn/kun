using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.MultiTenancy.EntityFrameworkCore.DependencyInjection;

/// <summary>Service-collection extensions for the EF Core side of Kun.MultiTenancy.</summary>
public static class KunMultiTenancyEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="KunTenantSaveChangesInterceptor"/> as a
    /// scoped service so consumers can attach it to their
    /// <c>DbContextOptionsBuilder.AddInterceptors(...)</c> call.
    /// Pair with <c>modelBuilder.ApplyKunMultiTenantFilters(this)</c> on an IKunMultiTenantDbContext
    /// in <c>OnModelCreating</c> for full read + write coverage.
    /// </summary>
    public static IServiceCollection AddKunMultiTenancyEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<KunTenantSaveChangesInterceptor>();
        return services;
    }
}
