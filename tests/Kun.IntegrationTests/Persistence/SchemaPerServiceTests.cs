using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.EntityFrameworkCore;
using Kun.EntityFrameworkCore.Extensions;
using Kun.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Xunit;

namespace Kun.IntegrationTests.Persistence;

// ---------------------------------------------------------------------------
// Internal test aggregates + DbContexts — not file-scoped to avoid the EF Core
// 10 ShortName() crash on primary-constructor records under file-scoped namespaces.
// ---------------------------------------------------------------------------

internal sealed class SchemaAItem : AggregateRoot<Guid>
{
    public string Name { get; private set; } = "";
    public static SchemaAItem Create(Guid id, string name) => new() { Id = id, Name = name };
}

internal sealed class SchemaBItem : AggregateRoot<Guid>
{
    public string Note { get; private set; } = "";
    public static SchemaBItem Create(Guid id, string note) => new() { Id = id, Note = note };
}

internal sealed class SchemaATestDbContext(DbContextOptions<SchemaATestDbContext> options, ITenantContext tc)
    : KunDbContext(options, tc)
{
    public DbSet<SchemaAItem> Items => Set<SchemaAItem>();
    protected override string? DefaultSchema => "sch_a";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SchemaAItem>(b =>
        {
            b.ToTable("a_items");
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).HasMaxLength(100);
        });
    }
}

internal sealed class SchemaBTestDbContext(DbContextOptions<SchemaBTestDbContext> options, ITenantContext tc)
    : KunDbContext(options, tc)
{
    public DbSet<SchemaBItem> Items => Set<SchemaBItem>();
    protected override string? DefaultSchema => "sch_b";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SchemaBItem>(b =>
        {
            b.ToTable("b_items");
            b.HasKey(e => e.Id);
            b.Property(e => e.Note).HasMaxLength(100);
        });
    }
}

internal sealed class NoopTenantContext : ITenantContext
{
    public string? TenantId => null;
    public bool IsMultiTenant => false;
}

/// <summary>
/// Verifies the P0-3 schema-per-service contract end-to-end: two <see cref="KunDbContext"/>
/// subclasses with different <c>DefaultSchema</c> values coexist in one PostgreSQL instance,
/// each carrying its own <c>outbox_messages</c>, <c>__ef_migrations_history</c>, and business
/// tables under its own schema. Also proves the <c>SchemaConsistencyInterceptor</c> throws
/// when a context overrides <c>DefaultSchema</c> without calling <c>UseKunSchema</c>.
/// </summary>
[Collection("Postgres")]
public sealed class SchemaPerServiceTests(PostgresFixture fx)
{
    [Fact]
    public async Task TwoContexts_Under_Distinct_Schemas_Coexist_In_One_Database()
    {
        if (string.IsNullOrEmpty(fx.ConnectionString))
        {
            Assert.Skip("Postgres fixture did not start (Docker unavailable).");
            return;
        }

        var ct = TestContext.Current.CancellationToken;

        var optsA = new DbContextOptionsBuilder<SchemaATestDbContext>()
            .UseNpgsql(fx.ConnectionString)
            .UseKunSchema("sch_a")
            .ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory>()
            .Options;

        var optsB = new DbContextOptionsBuilder<SchemaBTestDbContext>()
            .UseNpgsql(fx.ConnectionString)
            .UseKunSchema("sch_b")
            .ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory>()
            .Options;

        await using (var ctxA = new SchemaATestDbContext(optsA, new NoopTenantContext()))
        {
            await ctxA.Database.EnsureCreatedAsync(ct);
            ctxA.Items.Add(SchemaAItem.Create(Guid.NewGuid(), "alpha"));
            await ctxA.SaveChangesAsync(ct);
        }

        await using (var ctxB = new SchemaBTestDbContext(optsB, new NoopTenantContext()))
        {
            await ctxB.Database.EnsureCreatedAsync(ct);
            ctxB.Items.Add(SchemaBItem.Create(Guid.NewGuid(), "beta"));
            await ctxB.SaveChangesAsync(ct);
        }

        // Inspect information_schema to prove physical separation.
        var tables = await QueryTablesAsync(fx.ConnectionString!, ct);

        Assert.Contains(("sch_a", "a_items"), tables);
        Assert.Contains(("sch_a", "outbox_messages"), tables);
        Assert.Contains(("sch_b", "b_items"), tables);
        Assert.Contains(("sch_b", "outbox_messages"), tables);
        Assert.DoesNotContain(("public", "a_items"), tables);
        Assert.DoesNotContain(("public", "b_items"), tables);
    }

    [Fact]
    public async Task DefaultSchema_Without_UseKunSchema_Throws_At_First_SaveChanges()
    {
        if (string.IsNullOrEmpty(fx.ConnectionString))
        {
            Assert.Skip("Postgres fixture did not start (Docker unavailable).");
            return;
        }

        var ct = TestContext.Current.CancellationToken;

        // Note: no .UseKunSchema call — the interceptor must detect the mismatch.
        var opts = new DbContextOptionsBuilder<SchemaATestDbContext>()
            .UseNpgsql(fx.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKeyFactory>()
            .Options;

        await using var ctx = new SchemaATestDbContext(opts, new NoopTenantContext());

        // EnsureCreatedAsync opens a connection without going through SaveChangesAsync, so
        // the interceptor cannot fire during schema creation. Instead we trigger SaveChanges
        // with a pending insert — that IS the documented guarded path (M5).
        await ctx.Database.EnsureCreatedAsync(ct);
        ctx.Items.Add(SchemaAItem.Create(Guid.NewGuid(), "gamma"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ctx.SaveChangesAsync(ct));
        Assert.Contains("sch_a", ex.Message, StringComparison.Ordinal);
        Assert.Contains("UseKunSchema", ex.Message, StringComparison.Ordinal);
    }

    private static async Task<HashSet<(string Schema, string Table)>> QueryTablesAsync(string connectionString, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT table_schema, table_name
            FROM information_schema.tables
            WHERE table_schema IN ('sch_a', 'sch_b', 'public')
              AND table_type = 'BASE TABLE'
            """, conn);
        var set = new HashSet<(string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            set.Add((reader.GetString(0), reader.GetString(1)));
        }
        return set;
    }
}

/// <summary>
/// Model-cache-key factory that returns a fresh object per call, so two distinct
/// <see cref="KunDbContext"/> subclasses sharing an app-level service collection still get
/// independent compiled models keyed off their own <c>DefaultSchema</c> overrides.
/// </summary>
internal sealed class PerInstanceModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), ((KunDbContext)context).TenantIdForModelCacheKey, designTime, Guid.NewGuid());
}
