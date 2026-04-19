using FluentAssertions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus.DependencyInjection;
using Kun.Infrastructure.Abstractions;
using Kun.Mediator.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.Repository;

// ---------------------------------------------------------------------------
// Internal test types — NOT file-scoped to avoid EF Core 10 ShortName() crash.
// ---------------------------------------------------------------------------

internal sealed class SpecOrder : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public int Priority { get; private set; }

    public static SpecOrder Create(Guid id, string name, int priority = 0) =>
        new() { Id = id, Name = name, Priority = priority };
}

internal sealed class SpecTestDbContext : KunDbContext
{
    public DbSet<SpecOrder> Orders => Set<SpecOrder>();

    public SpecTestDbContext(DbContextOptions<SpecTestDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SpecOrder>(b =>
        {
            b.ToTable("spec_test_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(200);
            b.Property(o => o.Priority);
            b.Property(o => o.CreatedAt);
            b.Property(o => o.CreatedBy);
            b.Property(o => o.UpdatedAt);
            b.Property(o => o.UpdatedBy);
            b.Property(o => o.IsDeleted);
            b.Property(o => o.DeletedAt);
        });
    }
}

internal sealed class FindOrdersByNameSpec : Specification<SpecOrder>
{
    public FindOrdersByNameSpec(string name) => AddCriteria(o => o.Name == name);
}

internal sealed class OrdersOrderedByPrioritySpec : Specification<SpecOrder>
{
    public OrdersOrderedByPrioritySpec() => ApplyOrderBy(o => (object)o.Priority);
}

internal sealed class PagedOrdersSpec : Specification<SpecOrder>
{
    public PagedOrdersSpec(int skip, int take)
    {
        ApplyOrderBy(o => (object)o.Name);
        ApplyPaging(skip, take);
    }
}

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class SpecificationEvaluatorTests : IAsyncLifetime
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
        svc.AddDbContext<SpecTestDbContext>((_, opts) =>
            opts.UseNpgsql(_postgres.GetConnectionString()));
        svc.AddScoped<KunDbContext>(sp => sp.GetRequiredService<SpecTestDbContext>());
        svc.AddScoped<EfRepository<SpecOrder>>();
        _services = svc.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpecTestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task ListAsync_WithCriteriaSpec_ReturnsOnlyMatchingRows()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<EfRepository<SpecOrder>>();
        var db = scope.ServiceProvider.GetRequiredService<SpecTestDbContext>();

        await repo.AddAsync(SpecOrder.Create(Guid.NewGuid(), "Widget"), ct);
        await repo.AddAsync(SpecOrder.Create(Guid.NewGuid(), "Gadget"), ct);
        await repo.AddAsync(SpecOrder.Create(Guid.NewGuid(), "Doohickey"), ct);
        await db.SaveChangesAsync(ct);

        var results = await repo.ListAsync(new FindOrdersByNameSpec("Gadget"), ct);

        results.Should().HaveCount(1, "only the order named 'Gadget' matches the spec");
        results[0].Name.Should().Be("Gadget");
    }

    [Fact]
    public async Task ListAsync_WithOrderBySpec_ReturnsSortedRows()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<EfRepository<SpecOrder>>();
        var db = scope.ServiceProvider.GetRequiredService<SpecTestDbContext>();

        await repo.AddAsync(SpecOrder.Create(Guid.NewGuid(), "P3", priority: 30), ct);
        await repo.AddAsync(SpecOrder.Create(Guid.NewGuid(), "P1", priority: 10), ct);
        await repo.AddAsync(SpecOrder.Create(Guid.NewGuid(), "P2", priority: 20), ct);
        await db.SaveChangesAsync(ct);

        var results = await repo.ListAsync(new OrdersOrderedByPrioritySpec(), ct);

        results.Select(o => o.Priority).Should().BeInAscendingOrder(
            "the spec applies OrderBy(Priority) ascending");
    }

    [Fact]
    public async Task ListAsync_WithPagingSpec_ReturnsCorrectPage()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<EfRepository<SpecOrder>>();
        var db = scope.ServiceProvider.GetRequiredService<SpecTestDbContext>();

        for (var i = 1; i <= 5; i++)
        {
            await repo.AddAsync(SpecOrder.Create(Guid.NewGuid(), $"Paged-{i:D2}"), ct);
        }
        await db.SaveChangesAsync(ct);

        // Page 2 of size 2 (skip=2, take=2) — names are ordered A-Z so Paged-03 and Paged-04.
        var page = await repo.ListAsync(new PagedOrdersSpec(skip: 2, take: 2), ct);

        page.Should().HaveCount(2, "paging spec must limit to 2 items");
    }
}
