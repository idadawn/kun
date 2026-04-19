using FluentAssertions;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.Dapper;
using Kun.Dapper.Extensions;
using Kun.Dapper.Npgsql;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EventBus.DependencyInjection;
using Kun.Mediator.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.ReadSide;

// ---------------------------------------------------------------------------
// Internal test types — NOT file-scoped to avoid EF Core 10 ShortName() crash.
// ---------------------------------------------------------------------------

internal sealed class DapperOrder : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public int Amount { get; private set; }

    public static DapperOrder Create(Guid id, string name, int amount = 0) =>
        new() { Id = id, Name = name, Amount = amount };
}

internal sealed class DapperTestDbContext : KunDbContext
{
    public DbSet<DapperOrder> Orders => Set<DapperOrder>();

    public DapperTestDbContext(DbContextOptions<DapperTestDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DapperOrder>(b =>
        {
            b.ToTable("dapper_test_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(200);
            b.Property(o => o.Amount);
            b.Property(o => o.CreatedAt);
            b.Property(o => o.CreatedBy);
            b.Property(o => o.UpdatedAt);
            b.Property(o => o.UpdatedBy);
            b.Property(o => o.IsDeleted);
            b.Property(o => o.DeletedAt);
        });
    }
}

/// <summary>DTO projected from the read side — no EF Core entity.</summary>
internal sealed class OrderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Amount { get; set; }
}

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class DapperAotQueryTests : IAsyncLifetime
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

        var connectionString = _postgres.GetConnectionString();

        var svc = new ServiceCollection();
        svc.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        svc.AddKunMediator(_ => { });
        svc.AddKunEventBus(_ => { });
        svc.AddKunEntityFrameworkCore();
        svc.AddDbContext<DapperTestDbContext>((_, opts) =>
            opts.UseNpgsql(connectionString));
        svc.AddScoped<KunDbContext>(sp => sp.GetRequiredService<DapperTestDbContext>());
        svc.AddSingleton(new ReadDbOptions { ConnectionString = connectionString });
        svc.AddSingleton<IReadDbConnectionFactory, NpgsqlReadDbConnectionFactory>();
        _services = svc.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DapperTestDbContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is IAsyncDisposable ad) await ad.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task QueryPageAsync_ReturnsCorrectTotalCountAndItems()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed 5 orders via EF Core.
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DapperTestDbContext>();
            for (var i = 1; i <= 5; i++)
            {
                db.Orders.Add(DapperOrder.Create(Guid.NewGuid(), $"Dapper-Order-{i:D2}", amount: i * 10));
            }
            await db.SaveChangesAsync(ct);
        }

        // Query via Dapper on the read side.
        await using var readScope = _services.CreateAsyncScope();
        var factory = readScope.ServiceProvider.GetRequiredService<IReadDbConnectionFactory>();
        await using var connection = (System.Data.Common.DbConnection)await factory.OpenAsync(ct);

        const string countSql = "SELECT COUNT(*) FROM dapper_test_orders WHERE \"IsDeleted\" = false";
        const string pageSql  = """
            SELECT "Id", "Name", "Amount"
            FROM dapper_test_orders
            WHERE "IsDeleted" = false
            ORDER BY "Name"
            LIMIT @PageSize OFFSET @Offset
            """;

        var request = new PageRequest(1, 3);
        var result = await connection.QueryPageAsync<OrderDto>(countSql, pageSql, request, cancellationToken: ct);

        result.TotalCount.Should().Be(5, "5 rows were seeded");
        result.Items.Count.Should().Be(3, "page size is 3");
        result.HasNextPage.Should().BeTrue("page 1 of 2 has a next page");
    }

    [Fact]
    public async Task QueryPageAsync_SecondPage_ReturnsRemainingItems()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed 4 orders for pagination boundary test.
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DapperTestDbContext>();
            for (var i = 1; i <= 4; i++)
            {
                db.Orders.Add(DapperOrder.Create(Guid.NewGuid(), $"Page2-Order-{i:D2}", amount: i));
            }
            await db.SaveChangesAsync(ct);
        }

        await using var readScope = _services.CreateAsyncScope();
        var factory = readScope.ServiceProvider.GetRequiredService<IReadDbConnectionFactory>();
        await using var connection = (System.Data.Common.DbConnection)await factory.OpenAsync(ct);

        const string countSql = """SELECT COUNT(*) FROM dapper_test_orders WHERE "IsDeleted" = false AND "Name" LIKE 'Page2-%'""";
        const string pageSql  = """
            SELECT "Id", "Name", "Amount"
            FROM dapper_test_orders
            WHERE "IsDeleted" = false AND "Name" LIKE 'Page2-%'
            ORDER BY "Name"
            LIMIT @PageSize OFFSET @Offset
            """;

        var request = new PageRequest(2, 3);
        var result = await connection.QueryPageAsync<OrderDto>(countSql, pageSql, request, cancellationToken: ct);

        result.TotalCount.Should().Be(4, "4 matching rows were seeded");
        result.Items.Count.Should().Be(1, "page 2 of size 3 contains only 1 remaining item");
        result.HasNextPage.Should().BeFalse("page 2 is the last page");
    }
}
