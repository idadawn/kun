using Kun.Core.MultiTenancy;
using Kun.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kun.EntityFrameworkCore.Tests;

public sealed class KunDbContextSchemaTests
{
    private const string DesignConnectionString =
        "Host=kun-design-noop;Database=kun_test;Username=kun;Password=kun";

    private static DbContextOptions BuildOptions() =>
        new DbContextOptionsBuilder()
            .UseNpgsql(DesignConnectionString)
            .ReplaceService<IModelCacheKeyFactory, UncachedModelCacheKeyFactory>()
            .Options;

    [Fact]
    public void DefaultSchema_Override_Applies_HasDefaultSchema_To_Model()
    {
        using var ctx = new TestDbContext(BuildOptions(), NullTenantContext.Instance, schema: "catalog");

        Assert.Equal("catalog", ctx.Model.GetDefaultSchema());
    }

    [Fact]
    public void DefaultSchema_Null_Leaves_Model_DefaultSchema_Null()
    {
        using var ctx = new TestDbContext(BuildOptions(), NullTenantContext.Instance, schema: null);

        Assert.Null(ctx.Model.GetDefaultSchema());
    }

    [Fact]
    public void DefaultSchema_Propagates_To_Outbox_Messages_Entity()
    {
        using var ctx = new TestDbContext(BuildOptions(), NullTenantContext.Instance, schema: "order");

        var outbox = ctx.Model.FindEntityType(typeof(OutboxMessage));
        Assert.NotNull(outbox);
        Assert.Equal("order", outbox!.GetSchema());
    }

    [Fact]
    public void DefaultSchema_Values_Are_Independent_Per_Context_Instance()
    {
        using var catalog = new TestDbContext(BuildOptions(), NullTenantContext.Instance, schema: "catalog");
        using var order = new TestDbContext(BuildOptions(), NullTenantContext.Instance, schema: "order");

        Assert.Equal("catalog", catalog.Model.GetDefaultSchema());
        Assert.Equal("order", order.Model.GetDefaultSchema());
    }
}
