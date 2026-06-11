using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.IntegrationTests.Fixtures;

/// <summary>
/// Spins up a Postgres 16 container fronted by <c>edoburu/pgbouncer:latest</c> in
/// <c>POOL_MODE=transaction</c>. Exposes both direct and bouncer-fronted connection strings so
/// tests can assert that <c>SET LOCAL</c> / <c>set_config(..., true)</c> semantics survive
/// PgBouncer's backend-connection recycling between transactions.
/// </summary>
/// <remarks>
/// Shared between P1-4 (<c>TenantRlsInterceptorPgBouncerTests</c>, M2) and the later P1-5
/// PgBouncer compatibility suite. Skips automatically when Docker is not available.
/// </remarks>
public sealed class PgBouncerPostgresFixture : IAsyncLifetime
{
    private INetwork? _network;
    private PostgreSqlContainer? _postgres;
    private IContainer? _pgBouncer;

    /// <summary>Connection string that talks to Postgres directly (bypasses PgBouncer).</summary>
    public string DirectConnectionString { get; private set; } = string.Empty;

    /// <summary>Connection string routed through PgBouncer (transaction pool mode).</summary>
    public string BouncerConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        try
        {
            _network = new NetworkBuilder().WithName($"kun-rls-{Guid.NewGuid():N}").Build();
            await _network.CreateAsync(ct);

            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithNetwork(_network)
                .WithNetworkAliases("pg")
                .WithDatabase("kun")
                .WithUsername("kun")
                .WithPassword("kun_secret")
                .Build();
            await _postgres.StartAsync(ct);
            DirectConnectionString = _postgres.GetConnectionString();

            // Provision a non-superuser role with NOBYPASSRLS — PgBouncer will connect
            // to Postgres as this role so bouncer-routed queries cannot slip past RLS.
            // Superuser DDL still runs via DirectConnectionString.
            await ProvisionAppRoleAsync(ct);

            _pgBouncer = new ContainerBuilder()
                .WithImage("edoburu/pgbouncer:latest")
                .WithNetwork(_network)
                .WithEnvironment("DB_HOST", "pg")
                .WithEnvironment("DB_PORT", "5432")
                .WithEnvironment("DB_USER", "kun_app")
                .WithEnvironment("DB_PASSWORD", "app_secret")
                .WithEnvironment("DB_NAME", "kun")
                .WithEnvironment("POOL_MODE", "transaction")
                .WithEnvironment("AUTH_TYPE", "plain")
                // The 100-parallel stress test (PgBouncerCompatTests) drives 100
                // concurrent client conns. With NOSUPERUSER kun_app and a tiny pool of
                // 5 backend slots, queue pressure surfaces "Exception while reading from
                // stream" — pool exhaustion masquerading as a connection failure. 25
                // backend slots keeps the prepared-statement compatibility assertion
                // meaningful while staying well under postgres' default max_connections.
                .WithEnvironment("MAX_CLIENT_CONN", "200")
                .WithEnvironment("DEFAULT_POOL_SIZE", "25")
                .WithEnvironment("RESERVE_POOL_SIZE", "10")
                .WithEnvironment("RESERVE_POOL_TIMEOUT", "1")
                .WithEnvironment("LISTEN_PORT", "6432")
                .WithPortBinding(6432, assignRandomHostPort: true)
                // StartAsync alone returns when the container process launches, not
                // when pgbouncer has bound its listen socket. On loaded CI runners
                // that gap reaches whole seconds, and the first test connections hit
                // a half-started listener and die with kernel resets ("Exception
                // while reading from stream") faster than any in-test retry budget.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6432))
                .Build();
            await _pgBouncer.StartAsync(ct);

            // No Reset On Close=true + Max Auto Prepare=0 are required by PgBouncer's
            // transaction-pool mode: it recycles the backend connection between
            // transactions, so client-side prepared statements (Npgsql's auto-prepare)
            // and DISCARD ALL on close both break. See P1-5 / M6 of the gap-closure plan.
            BouncerConnectionString =
                $"Host=localhost;Port={_pgBouncer.GetMappedPublicPort(6432)};Database=kun;" +
                "Username=kun_app;Password=app_secret;No Reset On Close=true;Max Auto Prepare=0";
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker not available — PgBouncer integration test requires Docker (runs in CI).");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_pgBouncer is not null) await _pgBouncer.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
        if (_network is not null) await _network.DisposeAsync();
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
            GRANT ALL ON DATABASE kun TO kun_app;
            GRANT ALL ON SCHEMA public TO kun_app;
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO kun_app;
            """;

        await using var conn = new NpgsqlConnection(DirectConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
