using System.Net;
using System.Net.Sockets;
using Testcontainers.RabbitMq;
using Xunit;

namespace Kun.IntegrationTests.Fixtures;

/// <summary>
/// Per-class fixture that starts a RabbitMQ container for integration tests.
/// Skips automatically when Docker is unavailable.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    /// <summary>重启验收保持宿主端口不变；普通测试继续使用 Docker 随机端口。</summary>
    public bool PreservePortAcrossRestarts { get; init; }

    /// <summary>Gets the AMQP connection string once initialized.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Gets the underlying container instance (for stop/start in resilience tests).</summary>
    public RabbitMqContainer Container =>
        _container ?? throw new InvalidOperationException("Fixture not initialized.");

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        try
        {
            var builder = new RabbitMqBuilder().WithImage("rabbitmq:3.13-management");
            if (PreservePortAcrossRestarts)
            {
                // 先选取当前宿主的空闲端口，再将该端口写进 Docker HostConfig；
                // HostPort=0 会在每次 stop/start 时重新分配，不能用于连接恢复验收。
                using var reservation = new TcpListener(IPAddress.Loopback, 0);
                reservation.Start();
                var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
                reservation.Stop();
                builder = builder.WithPortBinding(port, 5672);
            }
            _container = builder.Build();
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
