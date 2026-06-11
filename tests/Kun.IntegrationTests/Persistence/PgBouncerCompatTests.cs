using System.Collections.Concurrent;
using Kun.IntegrationTests.Fixtures;
using Npgsql;
using Xunit;

namespace Kun.IntegrationTests.Persistence;

/// <summary>
/// Stress test for P1-5 / M6: 100 SELECTs (bounded to 30 in flight) through
/// PgBouncer in transaction-pool mode must NOT raise <c>prepared statement
/// does not exist</c> errors. The Npgsql compatibility flags
/// (<c>Max Auto Prepare=0</c>, <c>No Reset On Close=true</c>) set on
/// <see cref="PgBouncerPostgresFixture.BouncerConnectionString"/> are what
/// keep this stable — if either flag were ever dropped, Npgsql's auto-prepare
/// would reference backend-side names that no longer exist after PgBouncer
/// swaps the server connection.
/// </summary>
[Collection("PgBouncer")]
public sealed class PgBouncerCompatTests(PgBouncerPostgresFixture fx) : IAsyncLifetime
{
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS pgb_stress_ledger (
            id SERIAL PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            amount NUMERIC(18,4) NOT NULL
        );
        TRUNCATE pgb_stress_ledger;
        INSERT INTO pgb_stress_ledger (tenant_id, amount)
        SELECT 'T-' || (g % 10), (g * 1.25)::numeric(18,4)
        FROM generate_series(1, 500) g;
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
    public async Task Hundred_Parallel_Queries_Through_PgBouncer_Do_Not_Surface_Prepared_Statement_Errors()
    {
        if (string.IsNullOrEmpty(fx.BouncerConnectionString))
        {
            Assert.Skip("PgBouncer fixture did not start (Docker unavailable).");
            return;
        }

        var ct = TestContext.Current.CancellationToken;
        var errors = new ConcurrentBag<string>();

        // 100 simultaneous TCP+auth handshakes against single-threaded PgBouncer
        // overwhelm its accept loop on loaded shared CI runners; the kernel resets
        // queued connections, which Npgsql surfaces as transient "Exception while
        // reading from stream". That socket-layer storm is not what this test
        // asserts, so cap in-flight clients (still above the 25-slot backend pool,
        // so server-connection swapping keeps happening) and retry only transient
        // failures. PostgresException — including every prepared-statement error
        // this test exists to catch — is never retried.
        using var gate = new SemaphoreSlim(30);

        var work = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            // Rotate through three different query shapes to force Npgsql to consider
            // auto-prepare; if Max Auto Prepare=0 weren't honoured, the second-third
            // invocation of each shape would try to execute a stale prepared name.
            var tenant = $"T-{i % 10}";
            var shape = i % 3;
            var sql = shape switch
            {
                0 => "SELECT COUNT(*) FROM pgb_stress_ledger WHERE tenant_id = @t",
                1 => "SELECT COALESCE(SUM(amount), 0) FROM pgb_stress_ledger WHERE tenant_id = @t",
                _ => "SELECT MAX(amount) FROM pgb_stress_ledger WHERE tenant_id = @t",
            };

            await gate.WaitAsync(ct);
            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await using var conn = new NpgsqlConnection(fx.BouncerConnectionString);
                        await conn.OpenAsync(ct);

                        await using var cmd = new NpgsqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("t", tenant);
                        _ = await cmd.ExecuteScalarAsync(ct);
                        return;
                    }
                    catch (PostgresException ex)
                    {
                        errors.Add($"worker {i}: {ex.SqlState} {ex.MessageText}");
                        return;
                    }
                    catch (NpgsqlException ex) when (ex.IsTransient && attempt < 3)
                    {
                        await Task.Delay(50 * attempt, ct);
                    }
                    catch (NpgsqlException ex)
                    {
                        errors.Add($"worker {i}: {ex.GetType().Name} {ex.Message}");
                        return;
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }, ct));

        await Task.WhenAll(work);

        Assert.True(
            errors.IsEmpty,
            "PgBouncer compat stress test raised errors:\n" + string.Join("\n", errors));
    }
}
