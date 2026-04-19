using Testcontainers.Redis;
using Xunit;

namespace Kun.IntegrationTests.Fixtures;

/// <summary>
/// Per-class fixture that starts a Redis container for integration tests.
/// Skips automatically when Docker is unavailable.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    /// <summary>Gets the Redis connection string once initialized.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new RedisBuilder()
                .WithImage("redis:7-alpine")
                .Build();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available on this host — integration test requires Docker (runs in CI).");
            return;
        }

        await _container.StartAsync(TestContext.Current.CancellationToken);
        ConnectionString = _container.GetConnectionString();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
