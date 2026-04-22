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
    /// Walks <paramref name="modelBuilder"/>, finds every entity whose
    /// CLR type implements <see cref="IMultiTenant"/>, and attaches a
    /// <c>HasQueryFilter(e =&gt; e.TenantId == tenantContext.TenantId)</c>
    /// predicate.
    /// </summary>
    /// <remarks>
    /// Use this from <c>OnModelCreating</c> in a <see cref="DbContext"/>
    /// that does <b>not</b> inherit from <c>KunDbContext</c> — the base
    /// context already applies the same filter automatically.
    /// </remarks>
    public static ModelBuilder ApplyKunMultiTenantFilters(
        this ModelBuilder modelBuilder,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(tenantContext);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entity.ClrType;
            if (!typeof(IMultiTenant).IsAssignableFrom(clrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "e");
            var tenantIdProp = Expression.Property(parameter, nameof(IMultiTenant.TenantId));
            var currentTenantId = Expression.Property(
                Expression.Constant(tenantContext),
                nameof(ITenantContext.TenantId));
            var predicate = Expression.Equal(tenantIdProp, currentTenantId);
            modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(predicate, parameter));
        }

        return modelBuilder;
    }
}
