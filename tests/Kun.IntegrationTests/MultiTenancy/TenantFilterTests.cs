using FluentAssertions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus.DependencyInjection;
using Kun.Mediator.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.MultiTenancy;

// ---------------------------------------------------------------------------
// Internal test types — NOT file-scoped to avoid EF Core 10 ShortName() crash.
// ---------------------------------------------------------------------------

internal sealed class TenantOrder : AggregateRoot<Guid>, IMultiTenant
{
    public string Name { get; private set; } = string.Empty;
    public string? TenantId { get; private set; }

    public static TenantOrder Create(Guid id, string name, string tenantId) =>
        new() { Id = id, Name = name, TenantId = tenantId };
}

internal sealed class StubTenantContext : ITenantContext
{
    public StubTenantContext(string? tenantId)
    {
        TenantId = tenantId;
        IsMultiTenant = tenantId is not null;
    }

    public string? TenantId { get; }
    public bool IsMultiTenant { get; }
}

internal sealed class TenantTestDbContext : KunDbContext
{
    public DbSet<TenantOrder> Orders => Set<TenantOrder>();

    public TenantTestDbContext(DbContextOptions<TenantTestDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TenantOrder>(b =>
        {
            b.ToTable("tenant_test_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(200);
            b.Property(o => o.TenantId).HasMaxLength(100);
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
// Per-tenant IModelCacheKeyFactory so each tenant gets its own compiled model.
// EF Core's default factory keys only on DbContext type + designTime, meaning
// two providers with different ITenantContext values would share a single model
// and the query filter would be compiled with whichever tenant's context was
// first captured — breaking per-tenant isolation.
// ---------------------------------------------------------------------------

internal sealed class TenantAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    // Parameterless — EF Core's internal DI must be able to instantiate this.
    // The tenant ID is read from the KunDbContext.TenantIdForModelCacheKey property
    // (which reads from the application-injected ITenantContext) so each tenant
    // provider gets a distinct model cache key and thus its own compiled model.
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(),
         (context as KunDbContext)?.TenantIdForModelCacheKey,
         designTime);
}

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class TenantFilterTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private IServiceProvider _providerA = null!;
    private IServiceProvider _providerB = null!;

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

        _providerA = BuildProvider("tenant-a");
        _providerB = BuildProvider("tenant-b");

        // Create schema using provider A (schema is shared, tenant filter is per-query).
        await using var scope = _providerA.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantTestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_providerA is IAsyncDisposable adA) await adA.DisposeAsync();
        if (_providerB is IAsyncDisposable adB) await adB.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private ServiceProvider BuildProvider(string tenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new StubTenantContext(tenantId));
        services.AddKunMediator(_ => { });
        services.AddKunEventBus(_ => { });
        services.AddKunEntityFrameworkCore();
        services.AddDbContext<TenantTestDbContext>((_, opts) =>
        {
            opts.UseNpgsql(_postgres.GetConnectionString());

            // Replace the default IModelCacheKeyFactory so each tenant gets its own
            // compiled model. The factory reads the tenant ID from the DbContext instance
            // at Create time, producing a distinct cache key per tenant. Without this,
            // EF Core keys the model only on DbContext type — both providers share one
            // compiled model and the global query filter captures the first tenant's
            // ITenantContext, breaking per-tenant isolation.
            opts.ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>();
        });
        services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<TenantTestDbContext>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task TenantFilter_IsolatesRows_ByTenant()
    {
        var ct = TestContext.Current.CancellationToken;

        // Insert one order per tenant.
        await using (var scopeA = _providerA.CreateAsyncScope())
        {
            var db = scopeA.ServiceProvider.GetRequiredService<TenantTestDbContext>();
            db.Orders.Add(TenantOrder.Create(Guid.NewGuid(), "Order-A", "tenant-a"));
            await db.SaveChangesAsync(ct);
        }

        await using (var scopeB = _providerB.CreateAsyncScope())
        {
            var db = scopeB.ServiceProvider.GetRequiredService<TenantTestDbContext>();
            db.Orders.Add(TenantOrder.Create(Guid.NewGuid(), "Order-B", "tenant-b"));
            await db.SaveChangesAsync(ct);
        }

        // Query under tenant-a — must see only its own row.
        await using (var scopeA2 = _providerA.CreateAsyncScope())
        {
            var db = scopeA2.ServiceProvider.GetRequiredService<TenantTestDbContext>();
            var orders = await db.Orders.ToListAsync(ct);
            orders.Should().HaveCount(1, "tenant-a filter must exclude tenant-b row");
            orders[0].Name.Should().Be("Order-A");
        }

        // Query under tenant-b — must see only its own row.
        await using (var scopeB2 = _providerB.CreateAsyncScope())
        {
            var db = scopeB2.ServiceProvider.GetRequiredService<TenantTestDbContext>();
            var orders = await db.Orders.ToListAsync(ct);
            orders.Should().HaveCount(1, "tenant-b filter must exclude tenant-a row");
            orders[0].Name.Should().Be("Order-B");
        }
    }

    [Fact]
    public async Task TenantFilter_IgnoreQueryFilters_ReturnsAllTenants()
    {
        var ct = TestContext.Current.CancellationToken;

        // Insert orders for both tenants.
        await using (var scopeA = _providerA.CreateAsyncScope())
        {
            var db = scopeA.ServiceProvider.GetRequiredService<TenantTestDbContext>();
            db.Orders.Add(TenantOrder.Create(Guid.NewGuid(), "Order-X-A", "tenant-a"));
            await db.SaveChangesAsync(ct);
        }

        await using (var scopeB = _providerB.CreateAsyncScope())
        {
            var db = scopeB.ServiceProvider.GetRequiredService<TenantTestDbContext>();
            db.Orders.Add(TenantOrder.Create(Guid.NewGuid(), "Order-X-B", "tenant-b"));
            await db.SaveChangesAsync(ct);
        }

        // IgnoreQueryFilters on tenant-a provider must see both tenants.
        await using var scopeA2 = _providerA.CreateAsyncScope();
        var dbA = scopeA2.ServiceProvider.GetRequiredService<TenantTestDbContext>();
        var all = await dbA.Orders.IgnoreQueryFilters().ToListAsync(ct);
        all.Should().HaveCountGreaterThanOrEqualTo(2, "IgnoreQueryFilters bypasses the tenant filter");
    }
}
