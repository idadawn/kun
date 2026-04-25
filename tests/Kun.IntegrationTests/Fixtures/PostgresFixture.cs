using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.Fixtures;

/// <summary>
/// Per-class fixture that starts a Postgres 16 container for integration tests.
/// Skips automatically when Docker is unavailable.
/// </summary>
/// <remarks>
/// Exposes two connection strings:
/// <list type="bullet">
///   <item><description><see cref="ConnectionString"/> — the default Testcontainers user,
///   which is a PostgreSQL <c>SUPERUSER</c>. Used for DDL and seeding.</description></item>
///   <item><description><see cref="AppConnectionString"/> — a non-superuser
///   <c>NOBYPASSRLS</c> role (<c>kun_app</c>). Tests verifying Row-Level Security
///   enforcement must use this connection; <c>FORCE ROW LEVEL SECURITY</c> is bypassed
///   for superusers and <c>BYPASSRLS</c> roles, so the default user would render the
///   policy invisible to the test.</description></item>
/// </list>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Gets the superuser connection string (DDL, seeding).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the non-superuser (<c>NOBYPASSRLS</c>) connection string used by RLS
    /// policy tests. <see cref="string.Empty"/> if the container failed to start.
    /// </summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
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

        await _container.StartAsync(TestContext.Current.CancellationToken);
        ConnectionString = _container.GetConnectionString();

        await ProvisionAppRoleAsync(TestContext.Current.CancellationToken);
        AppConnectionString = BuildAppConnectionString(ConnectionString);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
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
            GRANT ALL ON DATABASE kun_test TO kun_app;
            GRANT ALL ON SCHEMA public TO kun_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO kun_app;
            """;

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Rewrites the superuser connection string to point at the <c>kun_app</c> role.
    /// </summary>
    private static string BuildAppConnectionString(string superuserConnectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(superuserConnectionString)
        {
            Username = "kun_app",
            Password = "app_secret",
        };
        return b.ConnectionString;
    }
}
