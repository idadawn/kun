using Kun.EventBus.RabbitMQ.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

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

            if (_connection is not null)
            {
                // 发布器和订阅器共享连接。网络恢复期间替换它会销毁消费者拓扑，
                // 而订阅循环仍在等待旧连接。保留自动恢复实例，调用方稍后重试。
                throw new IOException("RabbitMQ 连接正在恢复，暂不能创建发布通道。");
            }

            // CA1873 flags the argument computation, but masking is a handful
            // of IndexOf calls — and credentials must NEVER reach the sink.
#pragma warning disable CA1873
            Log.OpeningConnection(_logger, SanitizeUri(_options.ConnectionString));
#pragma warning restore CA1873

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_options.ConnectionString),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = _options.ConsumerName,
                // Use a short fixed timeout so an unreachable broker fails fast with
                // BrokerUnreachableException rather than hanging until the caller's
                // CancellationToken fires (which would produce OperationCanceledException
                // and silently skip AttemptCount increment in the outbox publisher loop).
                RequestedConnectionTimeout = TimeSpan.FromSeconds(2),
            };

            // Pass CancellationToken.None: we want a fast failure (BrokerUnreachableException)
            // on bad hosts rather than OperationCanceledException from the stopping token,
            // which the outbox publisher's catch guard would silently discard.
            _connection = await factory.CreateConnectionAsync(CancellationToken.None).ConfigureAwait(false);

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

    /// <summary>
    /// Masks the password in an AMQP URI for logging —
    /// <c>amqp://user:secret@host</c> → <c>amqp://user:***@host</c>.
    /// Credentials must never reach the log sink.
    /// </summary>
    internal static string SanitizeUri(string uri)
    {
        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        var at = uri.IndexOf('@', StringComparison.Ordinal);
        if (schemeEnd < 0 || at < 0)
        {
            return uri;
        }

        var colon = uri.IndexOf(':', schemeEnd + 3);
        if (colon < 0 || colon > at)
        {
            return uri;
        }

        return string.Concat(uri.AsSpan(0, colon + 1), "***", uri.AsSpan(at));
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
