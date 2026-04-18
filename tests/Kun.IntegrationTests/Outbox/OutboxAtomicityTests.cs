using FluentAssertions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus;
using Kun.EventBus.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.Outbox;

// ---------------------------------------------------------------------------
// Minimal test types (file-scoped — not visible outside this compilation unit)
// ---------------------------------------------------------------------------

file sealed class TestOrder : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;

    public static TestOrder Create(Guid id, string name) =>
        new() { Id = id, Name = name };
}

file sealed record TestOrderPlaced(Guid OrderId);

file sealed class TestDbContext : KunDbContext
{
    public DbSet<TestOrder> Orders => Set<TestOrder>();

    public TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TestOrder>(b =>
        {
            b.ToTable("test_orders");
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

public sealed class OutboxAtomicityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("kun_test")
        .WithUsername("kun")
        .WithPassword("kun_secret")
        .Build();

    private IServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        services.AddKunEventBus(b => b.Register<TestOrderPlaced>("test.order.placed.v1"));
        services.AddKunEntityFrameworkCore();
        services.AddDbContext<TestDbContext>((_, opts) =>
            opts.UseNpgsql(_postgres.GetConnectionString()));

        // EfUnitOfWork depends on KunDbContext — alias the concrete subclass.
        services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<TestDbContext>());

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is IAsyncDisposable ad) await ad.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Core M2 atomicity invariant: the aggregate row and the Outbox row are written
    /// in the same transaction by <see cref="EfUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    [Fact]
    public async Task SaveChanges_WithIntegrationEvent_PersistsBothAtomically()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<TestDbContext>();
        var eventBus = sp.GetRequiredService<IIntegrationEventBus>();
        var uow = sp.GetRequiredService<IUnitOfWork>();

        var order = TestOrder.Create(Guid.NewGuid(), "Widget");
        db.Orders.Add(order);
        eventBus.Publish(new TestOrderPlaced(order.Id));

        await uow.SaveChangesAsync(ct);

        // Query without filters so the soft-delete global filter doesn't hide rows.
        var orderCount = await db.Orders.IgnoreQueryFilters().CountAsync(ct);
        var outboxCount = await db.OutboxMessages
            .CountAsync(m => m.EventTypeId == "test.order.placed.v1", ct);

        orderCount.Should().Be(1, "the aggregate must be persisted");
        outboxCount.Should().Be(1, "the outbox message must be written in the same transaction");
    }

    /// <summary>
    /// Rollback invariant: when SaveChangesAsync throws (duplicate PK), neither the
    /// aggregate row nor the Outbox row must be committed.
    /// </summary>
    [Fact]
    public async Task Rollback_OnSaveFailure_LeavesNeitherRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var sharedId = Guid.NewGuid();

        // Seed one row so the second insert hits a PK collision.
        await using (var setup = _services.CreateAsyncScope())
        {
            var seedDb = setup.ServiceProvider.GetRequiredService<TestDbContext>();
            seedDb.Orders.Add(TestOrder.Create(sharedId, "Existing"));
            await seedDb.SaveChangesAsync(ct);
        }

        // In a fresh scope, attempt to insert a duplicate.
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var db2 = sp.GetRequiredService<TestDbContext>();
        var eventBus = sp.GetRequiredService<IIntegrationEventBus>();
        var uow = sp.GetRequiredService<IUnitOfWork>();

        db2.Orders.Add(TestOrder.Create(sharedId, "Duplicate"));
        eventBus.Publish(new TestOrderPlaced(sharedId));

        var act = async () => await uow.SaveChangesAsync(ct);
        await act.Should().ThrowAsync<Exception>("duplicate PK must cause a DB error");

        // Verify that NO outbox row was committed (rollback).
        await using var verify = _services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<TestDbContext>();
        var outboxCount = await verifyDb.OutboxMessages
            .CountAsync(m => m.EventTypeId == "test.order.placed.v1", ct);

        outboxCount.Should().Be(0, "rollback must leave no outbox rows");
    }
}
