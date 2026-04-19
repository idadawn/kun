using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kun.Sample.Catalog.Infrastructure;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c>.
/// Not used at runtime — the real DbContext is registered via DI in Program.cs.
/// </summary>
internal sealed class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    /// <inheritdoc/>
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=catalog_design;Username=postgres;Password=postgres")
            .Options;

        return new CatalogDbContext(options, NullTenantContext.Instance);
    }
}
