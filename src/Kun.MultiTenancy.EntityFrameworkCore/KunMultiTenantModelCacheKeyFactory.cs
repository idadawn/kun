using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kun.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// <see cref="IModelCacheKeyFactory"/> that emits a distinct compiled
/// model per tenant when the <see cref="DbContext"/> implements
/// <see cref="IKunMultiTenantDbContext"/>. Falls back to EF Core's
/// default key shape (context type + design-time flag) otherwise, so
/// consumers who forget to opt in get the same behaviour they had
/// before.
/// </summary>
/// <remarks>
/// <para>
/// Register on the <see cref="DbContextOptionsBuilder"/>:
/// <c>.ReplaceService&lt;IModelCacheKeyFactory, KunMultiTenantModelCacheKeyFactory&gt;()</c>.
/// </para>
/// <para>
/// Without this factory, EF Core keys compiled models only on
/// <see cref="DbContext"/> type. The first tenant's query filter is
/// then baked into the shared model — subsequent tenants would read
/// against the first tenant's filter constant, silently leaking data
/// across tenant boundaries.
/// </para>
/// </remarks>
public sealed class KunMultiTenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(),
         (context as IKunMultiTenantDbContext)?.TenantIdForModelCacheKey,
         designTime);
}
