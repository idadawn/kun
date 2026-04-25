using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.Saga.Persistence.Ef;
using Microsoft.EntityFrameworkCore;
using OrderAggregate = Kun.Sample.Order.Domain.Order;

namespace Kun.Sample.Order.Infrastructure;

/// <summary>
/// EF Core DbContext for the Order sample service.
/// Inherits <see cref="KunDbContext"/> to get OutboxMessages, tenant filters, and soft-delete filters.
/// Applies Kun Saga tables via <see cref="KunSagaDbContextExtensions.ApplyKunSagaConfiguration"/>.
/// </summary>
public sealed class OrderDbContext : KunDbContext
{
    /// <summary>Orders aggregate set.</summary>
    public DbSet<OrderAggregate> Orders => Set<OrderAggregate>();

    /// <summary>Creates a new <see cref="OrderDbContext"/>.</summary>
    public OrderDbContext(DbContextOptions<OrderDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    /// <inheritdoc/>
    protected override string? DefaultSchema => "order";

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new Configurations.OrderConfiguration());
        modelBuilder.ApplyKunSagaConfiguration();
    }
}
