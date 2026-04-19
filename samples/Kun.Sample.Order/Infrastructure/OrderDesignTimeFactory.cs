using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kun.Sample.Order.Infrastructure;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c>.
/// Not used at runtime — the real DbContext is registered via DI in Program.cs.
/// </summary>
internal sealed class OrderDesignTimeFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    /// <inheritdoc/>
    public OrderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql("Host=localhost;Database=order_design;Username=postgres;Password=postgres")
            .Options;

        return new OrderDbContext(options, NullTenantContext.Instance);
    }
}
