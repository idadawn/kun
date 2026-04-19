using Testcontainers.PostgreSql;
using Xunit;

namespace Kun.Sample.Tests.Fixtures;

/// <summary>
/// Per-collection fixture that starts a Postgres 16 container.
/// Skips automatically when Docker is unavailable.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Gets the connection string once the container is running.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithDatabase("sample_test")
                .WithUsername("sample")
                .WithPassword("sample_secret")
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available — integration tests require Docker (run in CI).");
            return;
        }

        try
        {
            await _container.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (
            ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("socket", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker daemon is not reachable — skipping integration test.");
            return;
        }

        ConnectionString = _container.GetConnectionString();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
