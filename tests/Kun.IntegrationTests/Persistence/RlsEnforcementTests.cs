using Kun.IntegrationTests.Fixtures;
using Npgsql;
using Xunit;

namespace Kun.IntegrationTests.Persistence;

/// <summary>
/// Verifies that PostgreSQL Row-Level Security policies block cross-tenant access even when a
/// caller bypasses the EF Core global query filter (e.g. via <c>IgnoreQueryFilters()</c>). The
/// test simulates what <see cref="Kun.EntityFrameworkCore.Interceptors.TenantRlsTransactionInterceptor"/>
/// does at runtime — a <c>set_config('app.current_tenant_id', @tenant, true)</c> call inside
/// each transaction — and asserts that the policy enforces isolation both under a tenant scope
/// and with no tenant GUC at all.
/// </summary>
[Collection("Postgres")]
public sealed class RlsEnforcementTests(PostgresFixture fx) : IAsyncLifetime
{
    private const string TableDdl = """
        CREATE TABLE IF NOT EXISTS rls_orders (
            id UUID PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            note TEXT NOT NULL
        );
        ALTER TABLE rls_orders ENABLE ROW LEVEL SECURITY;
        ALTER TABLE rls_orders FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS rls_tenant_iso ON rls_orders;
        CREATE POLICY rls_tenant_iso ON rls_orders
            USING (tenant_id = current_setting('app.current_tenant_id', true));
        """;

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(fx.ConnectionString)) return;

        var ct = TestContext.Current.CancellationToken;
        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(TableDdl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task IgnoreQueryFilters_Still_Blocked_By_Rls_Policy()
    {
        if (string.IsNullOrEmpty(fx.ConnectionString))
        {
            Assert.Skip("Postgres fixture did not start (Docker unavailable).");
            return;
        }

        var ct = TestContext.Current.CancellationToken;

        await SeedAsync("A", "row-A", ct);
        await SeedAsync("B", "row-B", ct);

        var seenByA = await ReadScopedToTenantAsync("A", ct);
        Assert.Single(seenByA);
        Assert.Equal("row-A", seenByA[0]);

        var seenByB = await ReadScopedToTenantAsync("B", ct);
        Assert.Single(seenByB);
        Assert.Equal("row-B", seenByB[0]);

        // No tenant GUC set → policy's USING clause evaluates tenant_id = NULL → 0 rows.
        // Defence-in-depth: a query that bypasses EF filters still cannot see any data.
        var seenUnscoped = await ReadUnscopedAsync(ct);
        Assert.Empty(seenUnscoped);
    }

    private async Task SeedAsync(string tenantId, string note, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetTenantGucAsync(conn, tx, tenantId, ct);

        await using var ins = new NpgsqlCommand(
            "INSERT INTO rls_orders (id, tenant_id, note) VALUES (@id, @t, @n)", conn, tx);
        ins.Parameters.AddWithValue("id", Guid.NewGuid());
        ins.Parameters.AddWithValue("t", tenantId);
        ins.Parameters.AddWithValue("n", note);
        await ins.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<List<string>> ReadScopedToTenantAsync(string tenantId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await SetTenantGucAsync(conn, tx, tenantId, ct);

        var results = new List<string>();
        await using var sel = new NpgsqlCommand("SELECT note FROM rls_orders ORDER BY note", conn, tx);
        await using var reader = await sel.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }
        await tx.CommitAsync(ct);
        return results;
    }

    private async Task<List<string>> ReadUnscopedAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync(ct);
        await using var sel = new NpgsqlCommand("SELECT note FROM rls_orders", conn);
        await using var reader = await sel.ExecuteReaderAsync(ct);
        var results = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }
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

[CollectionDefinition("Postgres")]
public sealed class PostgresFixtureGroup : ICollectionFixture<PostgresFixture>;
