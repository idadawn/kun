using System.Text;
using Kun.EventBus.Outbox;
using Kun.EventBus.RabbitMQ.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kun.EventBus.RabbitMQ.Publishing;

/// <summary>
/// Publishes <see cref="OutboxMessage"/> records to a RabbitMQ topic exchange.
/// Routing key equals <see cref="OutboxMessage.EventTypeId"/> (stable event ID, e.g. <c>order.placed.v1</c>).
/// W3C trace context headers and tenant metadata are forwarded as AMQP headers.
/// </summary>
public sealed partial class RabbitMqIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqIntegrationEventPublisher> _logger;

    /// <summary>Initializes the publisher.</summary>
    public RabbitMqIntegrationEventPublisher(
        RabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqIntegrationEventPublisher> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        // Declare the topic exchange (idempotent).
        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var props = new BasicProperties
        {
            Persistent = true,
            MessageId = message.Id.ToString(),
            Headers = BuildHeaders(message),
        };

        var body = Encoding.UTF8.GetBytes(message.Payload);

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: message.EventTypeId,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        Log.Published(_logger, message.EventTypeId, message.Id, _options.ExchangeName);
    }

    private static Dictionary<string, object?> BuildHeaders(OutboxMessage message)
    {
        var headers = new Dictionary<string, object?>
        {
            ["messageId"] = message.Id.ToString(),
            ["occurredAt"] = message.OccurredAt.ToString("O"),
        };

        if (message.TraceParent is not null)
        {
            headers["traceparent"] = message.TraceParent;
        }

        if (message.TraceState is not null)
        {
            headers["tracestate"] = message.TraceState;
        }

        if (message.TenantId is not null)
        {
            headers["tenantId"] = message.TenantId;
        }

        return headers;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 20, Level = LogLevel.Debug,
            Message = "Published {EventTypeId} (messageId={MessageId}) to exchange {Exchange}")]
        public static partial void Published(ILogger logger, string eventTypeId, Guid messageId, string exchange);
    }
}
