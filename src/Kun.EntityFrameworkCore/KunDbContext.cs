using System.Linq.Expressions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Kun.EntityFrameworkCore;

/// <summary>Base EF Core DbContext for Kun Framework services.</summary>
/// <remarks>
/// Applies automatic global query filters for <see cref="IMultiTenant"/> entities
/// (filters out rows whose <c>TenantId</c> does not match the current tenant scope)
/// and for <see cref="ISoftDelete"/> entities (excludes soft-deleted rows).
/// Ensures the Outbox is registered for atomic enqueue.
/// </remarks>
public abstract class KunDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    /// <summary>The Outbox message store.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Creates a new <see cref="KunDbContext"/>.</summary>
    protected KunDbContext(DbContextOptions options, ITenantContext tenantContext) : base(options)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        _tenantContext = tenantContext;
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        ApplyTenantFilters(modelBuilder);
        ApplySoftDeleteFilters(modelBuilder);
    }

    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IMultiTenant).IsAssignableFrom(entity.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entity.ClrType, "e");
            var tenantIdProp = Expression.Property(parameter, nameof(IMultiTenant.TenantId));
            var currentTenantId = Expression.Property(
                Expression.Constant(_tenantContext),
                nameof(ITenantContext.TenantId));

            // e.TenantId == _tenantContext.TenantId
            var filter = Expression.Lambda(
                Expression.Equal(tenantIdProp, currentTenantId),
                parameter);

            modelBuilder.Entity(entity.ClrType).HasQueryFilter(filter);
        }
    }

    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entity.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entity.ClrType, "e");
            var isDeletedProp = Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));

            // !e.IsDeleted
            var filter = Expression.Lambda(
                Expression.Not(isDeletedProp),
                parameter);

            modelBuilder.Entity(entity.ClrType).HasQueryFilter(filter);
        }
    }
}
