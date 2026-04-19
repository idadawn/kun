using FluentAssertions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus.DependencyInjection;
using Kun.Mediator.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.SoftDelete;

// ---------------------------------------------------------------------------
// Internal test types — NOT file-scoped to avoid EF Core 10 ShortName() crash.
// ---------------------------------------------------------------------------

internal sealed class SoftDeleteOrder : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;

    public static SoftDeleteOrder Create(Guid id, string name) =>
        new() { Id = id, Name = name };
}

internal sealed class SoftDeleteTestDbContext : KunDbContext
{
    public DbSet<SoftDeleteOrder> Orders => Set<SoftDeleteOrder>();

    public SoftDeleteTestDbContext(DbContextOptions<SoftDeleteTestDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SoftDeleteOrder>(b =>
        {
            b.ToTable("soft_delete_test_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(200);
            b.Property(o => o.CreatedAt);
            b.Property(o => o.CreatedBy);
            b.Property(o => o.UpdatedAt);
            b.Property(o => o.UpdatedBy);
            b.Property(o => o.IsDeleted);
            b.Property(o => o.DeletedAt);
        });
    }
}

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class SoftDeleteFilterTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private IServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithDatabase("kun_test")
                .WithUsername("kun")
                .WithPassword("kun_secret")
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available on this host — integration test requires Docker (runs in CI).");
            return;
        }

        await _postgres.StartAsync(TestContext.Current.CancellationToken);

        var svc = new ServiceCollection();
        svc.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        svc.AddKunMediator(_ => { });
        svc.AddKunEventBus(_ => { });
        svc.AddKunEntityFrameworkCore();
        svc.AddDbContext<SoftDeleteTestDbContext>((_, opts) =>
            opts.UseNpgsql(_postgres.GetConnectionString()));
        svc.AddScoped<KunDbContext>(sp => sp.GetRequiredService<SoftDeleteTestDbContext>());
        _services = svc.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SoftDeleteTestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task SoftDeleteFilter_ExcludesDeletedRows()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SoftDeleteTestDbContext>();

        var a = SoftDeleteOrder.Create(Guid.NewGuid(), "Alpha");
        var b = SoftDeleteOrder.Create(Guid.NewGuid(), "Beta");
        var c = SoftDeleteOrder.Create(Guid.NewGuid(), "Gamma");
        db.Orders.AddRange(a, b, c);
        await db.SaveChangesAsync(ct);

        // Soft-delete one record.
        c.Delete();
        db.Orders.Update(c);
        await db.SaveChangesAsync(ct);

        // Normal query must exclude the deleted row.
        var visible = await db.Orders.ToListAsync(ct);
        visible.Should().HaveCount(2, "the soft-deleted row must be filtered out");
        visible.Select(o => o.Name).Should().NotContain("Gamma");
    }

    [Fact]
    public async Task SoftDeleteFilter_IgnoreQueryFilters_ReturnsAllRows()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SoftDeleteTestDbContext>();

        var x = SoftDeleteOrder.Create(Guid.NewGuid(), "X-Alpha");
        var y = SoftDeleteOrder.Create(Guid.NewGuid(), "X-Beta");
        var z = SoftDeleteOrder.Create(Guid.NewGuid(), "X-Gamma");
        db.Orders.AddRange(x, y, z);
        await db.SaveChangesAsync(ct);

        z.Delete();
        db.Orders.Update(z);
        await db.SaveChangesAsync(ct);

        // IgnoreQueryFilters must return all 3 rows.
        var all = await db.Orders.IgnoreQueryFilters()
            .Where(o => o.Name.StartsWith("X-"))
            .ToListAsync(ct);
        all.Should().HaveCount(3, "IgnoreQueryFilters must bypass the soft-delete global filter");
        all.Should().Contain(o => o.IsDeleted, "the deleted row must be present when filters are ignored");
    }
}
