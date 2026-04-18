using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.Sample.Catalog.Domain;
using Kun.Sample.Catalog.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Kun.Sample.Catalog.Infrastructure;

/// <summary>EF Core DbContext for the Catalog bounded context.</summary>
public sealed class CatalogDbContext : KunDbContext
{
    /// <summary>All catalog products.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Initializes the CatalogDbContext.</summary>
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
    }
}
