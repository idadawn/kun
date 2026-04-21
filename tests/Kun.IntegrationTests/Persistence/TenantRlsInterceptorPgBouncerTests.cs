using Kun.IntegrationTests.Fixtures;
using Npgsql;
using Xunit;

namespace Kun.IntegrationTests.Persistence;

/// <summary>
/// M2 acceptance test for P1-4: exercises the <c>set_config('app.current_tenant_id', @t, true)</c>
/// + RLS-policy pair through PgBouncer in transaction-pool mode. PgBouncer recycles its backend
/// connection between transactions, so a GUC set outside an explicit transaction would leak
/// between tenants. Scoping with <c>is_local=true</c> (equivalent of <c>SET LOCAL</c>) ties the
/// value to the current transaction. This test simulates what
/// <c>TenantRlsTransactionInterceptor</c> does at runtime: emit the <c>set_config</c> inside
/// every fresh transaction, then run the tenant-visible query.
/// </summary>
[Collection("PgBouncer")]
public sealed class TenantRlsInterceptorPgBouncerTests(PgBouncerPostgresFixture fx) : IAsyncLifetime
{
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS pgb_rls_orders (
            id UUID PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            note TEXT NOT NULL
        );
        ALTER TABLE pgb_rls_orders ENABLE ROW LEVEL SECURITY;
        ALTER TABLE pgb_rls_orders FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS pgb_rls_iso ON pgb_rls_orders;
        CREATE POLICY pgb_rls_iso ON pgb_rls_orders
            USING (tenant_id = current_setting('app.current_tenant_id', true));
        """;

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(fx.DirectConnectionString)) return;

        var ct = TestContext.Current.CancellationToken;
        await using var conn = new NpgsqlConnection(fx.DirectConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TenantRlsInterceptor_WorksThroughPgBouncerTransactionMode()
    {
        if (string.IsNullOrEmpty(fx.BouncerConnectionString))
        {
            Assert.Skip("PgBouncer fixture did not start (Docker unavailable).");
            return;
        }

        var ct = TestContext.Current.CancellationToken;

        // Seed via direct connection (bypass PgBouncer).
        await SeedDirectAsync("A", "pgb-A", ct);
        await SeedDirectAsync("B", "pgb-B", ct);

        // Interleave reads across tenants through PgBouncer — each transaction picks a
        // potentially different backend connection from the pool. The per-transaction
        // set_config must isolate cleanly; leaking would show B's row when reading as A,
        // or duplicated rows.
        Assert.Equal(["pgb-A"], await ReadThroughBouncerAsync("A", ct));
        Assert.Equal(["pgb-B"], await ReadThroughBouncerAsync("B", ct));
        Assert.Equal(["pgb-A"], await ReadThroughBouncerAsync("A", ct));
        Assert.Equal(["pgb-B"], await ReadThroughBouncerAsync("B", ct));
    }

    private async Task SeedDirectAsync(string tenantId, string note, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(fx.DirectConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetTenantGucAsync(conn, tx, tenantId, ct);

        await using var ins = new NpgsqlCommand(
            "INSERT INTO pgb_rls_orders (id, tenant_id, note) VALUES (@id, @t, @n)", conn, tx);
        ins.Parameters.AddWithValue("id", Guid.NewGuid());
        ins.Parameters.AddWithValue("t", tenantId);
        ins.Parameters.AddWithValue("n", note);
        await ins.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<List<string>> ReadThroughBouncerAsync(string tenantId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(fx.BouncerConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetTenantGucAsync(conn, tx, tenantId, ct);

        var results = new List<string>();
        await using var sel = new NpgsqlCommand("SELECT note FROM pgb_rls_orders", conn, tx);
        await using var reader = await sel.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }
        await tx.CommitAsync(ct);
        return results;
    }

    private static async Task SetTenantGucAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string tenantId, CancellationToken ct)
    {
        await using var setCmd = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @t, true)", conn, tx);
        setCmd.Parameters.AddWithValue("t", tenantId);
        await setCmd.ExecuteNonQueryAsync(ct);
    }
}

[CollectionDefinition("PgBouncer")]
public sealed class PgBouncerFixtureGroup : ICollectionFixture<PgBouncerPostgresFixture>;
