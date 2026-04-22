using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
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

            _pgBouncer = new ContainerBuilder()
                .WithImage("edoburu/pgbouncer:latest")
                .WithNetwork(_network)
                .WithEnvironment("DB_HOST", "pg")
                .WithEnvironment("DB_PORT", "5432")
                .WithEnvironment("DB_USER", "kun")
                .WithEnvironment("DB_PASSWORD", "kun_secret")
                .WithEnvironment("DB_NAME", "kun")
                .WithEnvironment("POOL_MODE", "transaction")
                .WithEnvironment("AUTH_TYPE", "plain")
                .WithEnvironment("MAX_CLIENT_CONN", "100")
                .WithEnvironment("DEFAULT_POOL_SIZE", "5")
                .WithEnvironment("LISTEN_PORT", "6432")
                .WithPortBinding(6432, assignRandomHostPort: true)
                .Build();
            await _pgBouncer.StartAsync(ct);

            // No Reset On Close=true + Max Auto Prepare=0 are required by PgBouncer's
            // transaction-pool mode: it recycles the backend connection between
            // transactions, so client-side prepared statements (Npgsql's auto-prepare)
            // and DISCARD ALL on close both break. See P1-5 / M6 of the gap-closure plan.
            BouncerConnectionString =
                $"Host=localhost;Port={_pgBouncer.GetMappedPublicPort(6432)};Database=kun;" +
                "Username=kun;Password=kun_secret;No Reset On Close=true;Max Auto Prepare=0";
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
}
