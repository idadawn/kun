namespace Kun.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Marker interface for <c>DbContext</c>s that want the
/// <see cref="KunMultiTenantModelCacheKeyFactory"/> to partition EF
/// Core's compiled-model cache per tenant. Implementers expose the
/// ambient tenant id so the factory can emit a distinct cache key for
/// each tenant, preventing EF from folding the query-filter constant
/// across tenants on the first model build.
/// </summary>
/// <remarks>
/// <c>Kun.EntityFrameworkCore.KunDbContext</c> already exposes an
/// equivalent <c>TenantIdForModelCacheKey</c> property; custom
/// contexts that inherit from plain <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// should implement this interface to opt into per-tenant isolation.
/// </remarks>
public interface IKunMultiTenantDbContext
{
    /// <summary>Ambient tenant id, used purely as the EF model-cache key.</summary>
    string? TenantIdForModelCacheKey { get; }
}
