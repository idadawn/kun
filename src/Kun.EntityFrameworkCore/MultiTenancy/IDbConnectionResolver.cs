namespace Kun.EntityFrameworkCore.MultiTenancy;

/// <summary>
/// Resolves the connection string for a given tenant. Used by the
/// <see cref="Kun.Core.MultiTenancy.TenantIsolationStrategy.DatabasePerTenant"/> strategy
/// to dispatch DbContext construction to the tenant's dedicated database.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe (called concurrently from scoped request handlers)
/// and idempotent. The default <see cref="OptionsBasedTenantConnectionResolver"/> reads
/// a static tenant→connection-string map from <see cref="TenantsConnectionOptions"/>.
/// Larger deployments typically swap in a custom implementation backed by a secrets store
/// or a tenant-metadata service.
/// </remarks>
public interface IDbConnectionResolver
{
    /// <summary>Returns the connection string for <paramref name="tenantId"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no connection string is known for <paramref name="tenantId"/>.
    /// </exception>
    string ResolveConnectionString(string tenantId);
}
