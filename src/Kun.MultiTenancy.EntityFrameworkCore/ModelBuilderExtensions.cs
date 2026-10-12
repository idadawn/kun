using System.Linq.Expressions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Kun.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// <see cref="ModelBuilder"/> extensions that attach the Kun multi-tenant
/// global query filter to every <see cref="IMultiTenant"/> entity.
/// </summary>
public static class KunMultiTenancyModelBuilderExtensions
{
    /// <summary>
    /// Retained binary entry point for the unsafe accessor-only overload; rejects model creation.
    /// </summary>
    /// <remarks>
    /// An accessor alone cannot bind a reused model to the executing DbContext. Capturing its value
    /// would also be unsafe when the host uses EF's default shared model cache. Migrate to the
    /// DbContext overload; KunDbContext consumers already receive the safe filter automatically.
    /// </remarks>
    [Obsolete("Use ApplyKunMultiTenantFilters(this) with a DbContext implementing IKunMultiTenantDbContext. Accessor-only filters cannot safely bind a shared model.", DiagnosticId = "KUNTENANT001")]
    public static ModelBuilder ApplyKunMultiTenantFilters(
        this ModelBuilder modelBuilder,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(tenantContext);

        throw new InvalidOperationException("Accessor-only tenant filters cannot bind the executing DbContext. Use ApplyKunMultiTenantFilters(this) with IKunMultiTenantDbContext.");
    }

    /// <summary>
    /// Attaches the tenant predicate to the executing DbContext so a shared model never reads
    /// a previous context's tenant accessor. Call with <c>modelBuilder.ApplyKunMultiTenantFilters(this)</c>.
    /// </summary>
    public static ModelBuilder ApplyKunMultiTenantFilters<TContext>(this ModelBuilder modelBuilder, TContext context)
        where TContext : DbContext, IKunMultiTenantDbContext
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);
        var currentTenantId = Expression.Property(
            Expression.Convert(Expression.Constant(context, typeof(TContext)), typeof(IKunMultiTenantDbContext)),
            nameof(IKunMultiTenantDbContext.TenantIdForModelCacheKey));
        return Apply(modelBuilder, currentTenantId);
    }

    private static ModelBuilder Apply(ModelBuilder modelBuilder, Expression currentTenantId)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entity.ClrType;
            if (!typeof(IMultiTenant).IsAssignableFrom(clrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "e");
            var tenantIdProp = Expression.Property(parameter, nameof(IMultiTenant.TenantId));
            var predicate = Expression.Equal(tenantIdProp, currentTenantId);
            modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(predicate, parameter));
        }

        return modelBuilder;
    }
}
