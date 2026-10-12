using FluentAssertions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.MultiTenancy.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kun.MultiTenancy.Tests;

public sealed class EntityFrameworkIntegrationTests
{
    [Fact]
    public void GlobalFilter_AccessorOnlyOverload_RejectsUnsafeModelCreation()
    {
        var builder = new ModelBuilder();
        var tenant = new AsyncLocalTenantContext();
#pragma warning disable KUNTENANT001 // 验证旧入口明确拒绝，而不是生成会引用其它宿主的模型。
        var apply = () => builder.ApplyKunMultiTenantFilters(tenant);
#pragma warning restore KUNTENANT001
        apply.Should().Throw<InvalidOperationException>().WithMessage("*DbContext*");
    }

    [Fact]
    public async Task GlobalFilter_HidesOtherTenants()
    {
        var ctx = new AsyncLocalTenantContext();
        var ct = TestContext.Current.CancellationToken;

        // Seed with tenant-a active
        using (ctx.BeginScope("tenant-a"))
        {
            using var db = CreateDb(ctx);
            db.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "a-one" });
            db.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "a-two" });
            await db.SaveChangesAsync(ct);
        }

        // Add a tenant-b row on the same in-memory database name.
        using (ctx.BeginScope("tenant-b"))
        {
            using var db = CreateDb(ctx);
            db.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "b-one" });
            await db.SaveChangesAsync(ct);
        }

        // Tenant A sees only A's rows
        using (ctx.BeginScope("tenant-a"))
        {
            using var db = CreateDb(ctx);
            var names = await db.Widgets.Select(w => w.Name).ToListAsync(ct);
            names.Should().BeEquivalentTo(["a-one", "a-two"]);
        }

        // Tenant B sees only B's rows
        using (ctx.BeginScope("tenant-b"))
        {
            using var db = CreateDb(ctx);
            var names = await db.Widgets.Select(w => w.Name).ToListAsync(ct);
            names.Should().BeEquivalentTo(["b-one"]);
        }
    }

    [Fact]
    public async Task Interceptor_AutoFillsTenantIdOnInsert()
    {
        var ctx = new AsyncLocalTenantContext();
        var ct = TestContext.Current.CancellationToken;

        using (ctx.BeginScope("tenant-x"))
        {
            using var db = CreateDb(ctx);
            // Consumer forgot to set TenantId — the interceptor fills it in.
            db.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "auto-filled" });
            await db.SaveChangesAsync(ct);
        }

        // Verify by escaping the filter on a fresh db context.
        using (ctx.BeginScope("tenant-x"))
        {
            using var db = CreateDb(ctx);
            var widget = await db.Widgets.IgnoreQueryFilters()
                .SingleAsync(w => w.Name == "auto-filled", ct);
            widget.TenantId.Should().Be("tenant-x");
        }
    }

    [Fact]
    public async Task Interceptor_PreservesExplicitTenantId()
    {
        var ctx = new AsyncLocalTenantContext();
        var ct = TestContext.Current.CancellationToken;

        using (ctx.BeginScope("tenant-y"))
        {
            using var db = CreateDb(ctx);
            db.Widgets.Add(new Widget
            {
                Id = Guid.NewGuid(),
                Name = "explicit",
                TenantId = "tenant-override",
            });
            await db.SaveChangesAsync(ct);
        }

        using (ctx.BeginScope("tenant-override"))
        {
            using var db = CreateDb(ctx);
            var widget = await db.Widgets.SingleAsync(w => w.Name == "explicit", ct);
            widget.TenantId.Should().Be("tenant-override");
        }
    }

    private readonly string _dbName = $"mt-tests-{Guid.NewGuid():N}";

    private TestDbContext CreateDb(ITenantContext tenantContext)
    {
        var interceptor = new KunTenantSaveChangesInterceptor(
            tenantContext,
            NullLogger<KunTenantSaveChangesInterceptor>.Instance);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(_dbName)
            .AddInterceptors(interceptor)
            .ReplaceService<IModelCacheKeyFactory, KunMultiTenantModelCacheKeyFactory>()
            .Options;

        return new TestDbContext(options, tenantContext);
    }

    private sealed class Widget : IMultiTenant
    {
        public Guid Id { get; init; }
        public string Name { get; set; } = string.Empty;
        public string? TenantId { get; set; }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenantContext)
        : DbContext(options), IKunMultiTenantDbContext
    {
        private readonly ITenantContext _tenantContext = tenantContext;

        public DbSet<Widget> Widgets => Set<Widget>();

        public string? TenantIdForModelCacheKey => _tenantContext.TenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Widget>().HasKey(w => w.Id);
            modelBuilder.ApplyKunMultiTenantFilters(this);
        }
    }
}
