using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Kun.EventBus.RabbitMQ.Options;

namespace Kun.EventBus.RabbitMQ;

/// <summary>
/// Singleton that owns and lazily opens the <see cref="IConnection"/> to RabbitMQ.
/// Automatic recovery is enabled via <see cref="ConnectionFactory.AutomaticRecoveryEnabled"/>.
/// </summary>
public sealed partial class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private IConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Initializes the provider with the supplied options.</summary>
    public RabbitMqConnectionProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Returns the open connection, lazily creating it on first call.</summary>
    public async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            Log.OpeningConnection(_logger, _options.ConnectionString);

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_options.ConnectionString),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = _options.ConsumerName,
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            Log.ConnectionEstablished(_logger);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connection.Dispose();
            _connection = null;
        }

        _lock.Dispose();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 10, Level = LogLevel.Information,
            Message = "Opening RabbitMQ connection to {Uri}")]
        public static partial void OpeningConnection(ILogger logger, string uri);

        [LoggerMessage(EventId = 11, Level = LogLevel.Information,
            Message = "RabbitMQ connection established.")]
        public static partial void ConnectionEstablished(ILogger logger);
    }
}
