using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kun.EntityFrameworkCore.Tests.MultiTenancy;

public sealed class TenantAwareModelCacheKeyFactoryTests
{
    [Fact]
    public void Create_NullContext_Throws()
    {
        var factory = new TenantAwareModelCacheKeyFactory();
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, designTime: false));
    }

    [Fact]
    public void Create_TwoTenants_ProducesDistinctKeys()
    {
        var factory = new TenantAwareModelCacheKeyFactory();
        using var ctxA = BuildContext(tenantId: "tenant-a");
        using var ctxB = BuildContext(tenantId: "tenant-b");

        var keyA = factory.Create(ctxA, designTime: false);
        var keyB = factory.Create(ctxB, designTime: false);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Create_SameTenant_ProducesEqualKeys()
    {
        var factory = new TenantAwareModelCacheKeyFactory();
        using var ctx1 = BuildContext(tenantId: "tenant-a");
        using var ctx2 = BuildContext(tenantId: "tenant-a");

        var key1 = factory.Create(ctx1, designTime: false);
        var key2 = factory.Create(ctx2, designTime: false);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Create_NullTenant_UsesPlaceholderSoKeyIsStable()
    {
        var factory = new TenantAwareModelCacheKeyFactory();
        using var ctx1 = BuildContext(tenantId: null);
        using var ctx2 = BuildContext(tenantId: null);

        var key1 = factory.Create(ctx1, designTime: false);
        var key2 = factory.Create(ctx2, designTime: false);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Create_DesignTimeFlag_ProducesDistinctKeys()
    {
        var factory = new TenantAwareModelCacheKeyFactory();
        using var ctx = BuildContext(tenantId: "tenant-a");

        var runtimeKey = factory.Create(ctx, designTime: false);
        var designKey = factory.Create(ctx, designTime: true);

        Assert.NotEqual(runtimeKey, designKey);
    }

    [Fact]
    public void Create_NonKunDbContext_ReturnsTupleKey()
    {
        var factory = new TenantAwareModelCacheKeyFactory();
        using var plain = new PlainDbContext();

        var key = factory.Create(plain, designTime: false);

        // Non-KunDbContext fallback is (Type, bool) tuple.
        Assert.Equal((typeof(PlainDbContext), false), key);
    }

    // Unreachable Npgsql host: the factory tests only call Create() — they never query, so
    // the connection is never opened. Keeps us off InMemory (which would break
    // OutboxMessageConfiguration.ToTable()) and off SQLite (another dependency).
    private const string UnreachableConnectionString =
        "Host=kun-factory-noop.invalid;Database=kun_test;Username=kun;Password=kun;Timeout=2;Command Timeout=2";

    private static TestDbContext BuildContext(string? tenantId)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(UnreachableConnectionString)
            .ReplaceService<IModelCacheKeyFactory, UncachedModelCacheKeyFactory>()
            .Options;
        var tenantContext = new FakeTenantContext(tenantId);
        return new TestDbContext(options, tenantContext, schema: null);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string? tenantId)
        {
            TenantId = tenantId;
        }
        public string? TenantId { get; }
        public bool IsMultiTenant => TenantId is not null;
    }

    private sealed class PlainDbContext : DbContext
    {
        public PlainDbContext() : base(new DbContextOptionsBuilder<PlainDbContext>()
            .UseNpgsql(UnreachableConnectionString)
            .Options)
        { }
    }
}
