using FluentAssertions;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.DependencyInjection;
using Kun.EntityFrameworkCore.Interceptors;
using Kun.EventBus.DependencyInjection;
using Kun.Mediator.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.MultiTenancy;

// ---------------------------------------------------------------------------
// A plain row — deliberately NOT implementing IMultiTenant so the EF global
// query filter does NOT fire. That way the isolation observed in the tests
// is entirely the PostgreSQL RLS policy + the TenantRlsConnectionInterceptor,
// proving the defence-in-depth layer on its own.
// ---------------------------------------------------------------------------

internal sealed class RlsOrder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}

internal sealed class RlsTestDbContext : KunDbContext
{
    public DbSet<RlsOrder> Orders => Set<RlsOrder>();

    public RlsTestDbContext(DbContextOptions<RlsTestDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<RlsOrder>(b =>
        {
            b.ToTable("rls_test_orders");
            b.HasKey(o => o.Id);
            // Column names must match the RLS policy literal "tenant_id".
            // EF default PascalCase would emit "TenantId" and the policy's
            // USING (tenant_id = ...) clause would error with
            // 42703: column "tenant_id" does not exist.
            b.Property(o => o.Id).HasColumnName("id");
            b.Property(o => o.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(o => o.TenantId).HasColumnName("tenant_id").HasMaxLength(100).IsRequired();
        });
    }
}

public sealed class TenantRlsTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private IServiceProvider _providerA = null!;
    private IServiceProvider _providerB = null!;
    private string _appConnectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithDatabase("kun_rls_test")
                .WithUsername("kun")
                .WithPassword("kun_secret")
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available on this host — integration test requires Docker (runs in CI).");
            return;
        }

        await _postgres.StartAsync(ct);

        // Provision a non-superuser NOBYPASSRLS role. The default Testcontainers user
        // is a SUPERUSER — FORCE ROW LEVEL SECURITY does not bind superusers, so the
        // RLS policy below would be silently invisible without this extra role.
        await ProvisionAppRoleAsync(ct);
        _appConnectionString = BuildAppConnectionString(_postgres.GetConnectionString());

        // Bootstrap: DDL runs under the superuser connection so CREATE POLICY /
        // ALTER TABLE / GRANT succeed. Table owner is the superuser; FORCE RLS is
        // required so the owner path still evaluates the policy.
        await BootstrapSchemaAsync(ct);

        // Tenant-scoped providers query as kun_app (non-superuser). That's the
        // connection identity the RLS policy actually binds to.
        _providerA = BuildProvider("tenant-a");
        _providerB = BuildProvider("tenant-b");
    }

    private async Task ProvisionAppRoleAsync(CancellationToken ct)
    {
        const string ddl = """
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kun_app') THEN
                    CREATE ROLE kun_app LOGIN PASSWORD 'app_secret' NOSUPERUSER NOBYPASSRLS;
                END IF;
            END $$;
            GRANT ALL ON DATABASE kun_rls_test TO kun_app;
            GRANT ALL ON SCHEMA public TO kun_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO kun_app;
            """;
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task BootstrapSchemaAsync(CancellationToken ct)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContext>(new StubTenantContext("bootstrap"))
            .AddKunMediator(_ => { })
            .AddKunEventBus(_ => { })
            .AddKunEntityFrameworkCore()
            .AddDbContext<RlsTestDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString()))
            .AddScoped<KunDbContext>(sp => sp.GetRequiredService<RlsTestDbContext>())
            .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE rls_test_orders ENABLE ROW LEVEL SECURITY;
            ALTER TABLE rls_test_orders FORCE ROW LEVEL SECURITY;
            CREATE POLICY rls_tenant_scope ON rls_test_orders
                USING      (tenant_id = current_setting('app.current_tenant_id', true))
                WITH CHECK (tenant_id = current_setting('app.current_tenant_id', true));
            GRANT SELECT, INSERT, UPDATE, DELETE ON rls_test_orders TO kun_app;
            """, ct);
    }

    private static string BuildAppConnectionString(string superuserConnectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(superuserConnectionString)
        {
            Username = "kun_app",
            Password = "app_secret",
        };
        return b.ConnectionString;
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
        services.AddLogging(b => b.AddFilter(
            "Kun.EntityFrameworkCore.Interceptors", LogLevel.Warning));
        services.AddSingleton<ITenantContext>(new StubTenantContext(tenantId));
        services.AddKunMediator(_ => { });
        services.AddKunEventBus(_ => { });
        services.AddKunEntityFrameworkCore();
        services.AddDbContext<RlsTestDbContext>((sp, opts) =>
        {
            // Non-superuser connection — the kun_app role has NOBYPASSRLS, so FORCE
            // ROW LEVEL SECURITY binds it and the policy actually isolates rows.
            opts.UseNpgsql(_appConnectionString);
            opts.AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
            // Per-tenant model cache — same rationale as TenantFilterTests.
            opts.ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>();
        });
        services.AddScoped<KunDbContext>(sp => sp.GetRequiredService<RlsTestDbContext>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Rls_Policy_Isolates_Rows_Across_Tenants_Without_Ef_Filter()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var scopeA = _providerA.CreateAsyncScope())
        {
            var db = scopeA.ServiceProvider.GetRequiredService<RlsTestDbContext>();
            db.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), Name = "Order-A", TenantId = "tenant-a" });
            await db.SaveChangesAsync(ct);
        }

        await using (var scopeB = _providerB.CreateAsyncScope())
        {
            var db = scopeB.ServiceProvider.GetRequiredService<RlsTestDbContext>();
            db.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), Name = "Order-B", TenantId = "tenant-b" });
            await db.SaveChangesAsync(ct);
        }

        // Tenant-A scope sees only its row — the entity has no IMultiTenant marker,
        // so EF's global query filter does NOT fire. Any isolation here is PG RLS.
        await using (var scopeA2 = _providerA.CreateAsyncScope())
        {
            var db = scopeA2.ServiceProvider.GetRequiredService<RlsTestDbContext>();
            var orders = await db.Orders.ToListAsync(ct);
            orders.Should().HaveCount(1, "RLS policy must hide tenant-b's row from tenant-a");
            orders[0].Name.Should().Be("Order-A");
        }

        await using (var scopeB2 = _providerB.CreateAsyncScope())
        {
            var db = scopeB2.ServiceProvider.GetRequiredService<RlsTestDbContext>();
            var orders = await db.Orders.ToListAsync(ct);
            orders.Should().HaveCount(1, "RLS policy must hide tenant-a's row from tenant-b");
            orders[0].Name.Should().Be("Order-B");
        }
    }

    [Fact]
    public async Task Rls_Policy_Blocks_Cross_Tenant_Write_Attempts()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _providerA.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        // Tenant-A's context inserts a row labelled tenant-b — the RLS WITH CHECK
        // clause must cause the INSERT to fail before reaching the table.
        db.Orders.Add(new RlsOrder
        {
            Id = Guid.NewGuid(),
            Name = "Forged",
            TenantId = "tenant-b",
        });

        var act = async () => await db.SaveChangesAsync(ct);
        await act.Should().ThrowAsync<DbUpdateException>(
            "RLS WITH CHECK must reject cross-tenant inserts");
    }

    [Fact]
    public async Task Interceptor_Rejects_Unsafe_TenantId_Values()
    {
        // Build a provider with a tenant id that fails the interceptor's whitelist.
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContext>(new StubTenantContext("evil'; DROP TABLE foo; --"))
            .AddKunMediator(_ => { })
            .AddKunEventBus(_ => { })
            .AddKunEntityFrameworkCore()
            .AddDbContext<RlsTestDbContext>((sp, opts) =>
            {
                opts.UseNpgsql(_appConnectionString);
                opts.AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
                opts.ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>();
            })
            .AddScoped<KunDbContext>(sp => sp.GetRequiredService<RlsTestDbContext>())
            .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        var act = async () => await db.Orders.ToListAsync(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>(
            "interceptor must refuse to inline an unsafe tenant id into SET");
    }
}
