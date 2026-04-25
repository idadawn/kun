using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kun.EntityFrameworkCore.MultiTenancy;

/// <summary>
/// <see cref="IModelCacheKeyFactory"/> that partitions EF Core's compiled-model cache by
/// tenant ID in addition to context type and design-time flag. Required for the
/// <see cref="Kun.Core.MultiTenancy.TenantIsolationStrategy.SchemaPerTenant"/> strategy so
/// that tenant A's schema-specific model is not reused for tenant B.
/// </summary>
/// <remarks>
/// <para>
/// Reads the tenant ID from <see cref="KunDbContext.TenantIdForModelCacheKey"/>. Contexts
/// that do not derive from <see cref="KunDbContext"/> fall back to the default type-only
/// cache key, so registering this factory is safe for mixed contexts in one host.
/// </para>
/// <para>
/// When <c>TenantIdForModelCacheKey</c> is <see langword="null"/> (e.g. background task
/// with no scoped tenant), the key uses the placeholder <c>"__kun_no_tenant__"</c> so a
/// single shared "no tenant" model gets cached rather than rebuilding on every call.
/// </para>
/// </remarks>
public sealed class TenantAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    private const string NoTenantPlaceholder = "__kun_no_tenant__";

    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is KunDbContext kun)
        {
            return new TenantModelCacheKey(
                context.GetType(),
                kun.TenantIdForModelCacheKey ?? NoTenantPlaceholder,
                designTime);
        }

        return (context.GetType(), designTime);
    }

    private readonly record struct TenantModelCacheKey(Type ContextType, string TenantId, bool DesignTime);
}
