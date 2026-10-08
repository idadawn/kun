using System.Diagnostics;
using System.Text;
using Kun.Core.MultiTenancy;
using Kun.EventBus.Idempotency;
using Kun.EventBus.RabbitMQ.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kun.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Background service that subscribes to RabbitMQ topic queues and dispatches received messages
/// to registered <see cref="IIntegrationEventHandler{TEvent}"/> implementations.
/// Idempotency is enforced via <see cref="IProcessedMessageStore"/>.
/// W3C Trace Context is restored from AMQP headers before handler dispatch.
/// </summary>
public sealed partial class RabbitMqIntegrationEventSubscriber : BackgroundService
{
    private static readonly ActivitySource ActivitySource =
        new(nameof(RabbitMqIntegrationEventSubscriber));

    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventTypeRegistry _registry;
    private readonly RabbitMqOptions _options;
    private readonly IReadOnlyList<SubscriptionRegistration> _subscriptions;
    private readonly ILogger<RabbitMqIntegrationEventSubscriber> _logger;
    private IChannel? _channel;

    internal RabbitMqIntegrationEventSubscriber(
        RabbitMqConnectionProvider connectionProvider,
        IServiceScopeFactory scopeFactory,
        IEventTypeRegistry registry,
        IOptions<RabbitMqOptions> options,
        IEnumerable<SubscriptionRegistration> subscriptions,
        ILogger<RabbitMqIntegrationEventSubscriber> logger)
    {
        _connectionProvider = connectionProvider;
        _scopeFactory = scopeFactory;
        _registry = registry;
        _options = options.Value;
        _subscriptions = [.. subscriptions];
        _logger = logger;
    }

    private static readonly TimeSpan InitialConnectBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxConnectBackoff = TimeSpan.FromSeconds(30);
    /// <summary>A consume session that survived this long resets the backoff.</summary>
    private static readonly TimeSpan StableSessionThreshold = TimeSpan.FromMinutes(1);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriptions.Count == 0)
        {
            Log.NoSubscriptions(_logger);
            return;
        }

        // Resilient consume loop: an unreachable broker (at startup, or a
        // connect/topology failure automatic recovery cannot repair) must
        // never fault this BackgroundService — the .NET host default
        // (BackgroundServiceExceptionBehavior.StopHost) would take the whole
        // consumer host down with the broker. Retry with capped backoff
        // until the host stops.
        var backoff = InitialConnectBackoff;
        while (!stoppingToken.IsCancellationRequested)
        {
            var sessionStarted = TimeProvider.System.GetTimestamp();
            try
            {
                await ConsumeUntilStoppedAsync(stoppingToken).ConfigureAwait(false);
                return; // clean stop via stoppingToken inside the session
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (TimeProvider.System.GetElapsedTime(sessionStarted) >= StableSessionThreshold)
                {
                    backoff = InitialConnectBackoff;
                }

                Log.ConsumeSessionFailed(_logger, ex, backoff.TotalSeconds);
            }

            try
            {
                await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxConnectBackoff.Ticks));
        }
    }

    private async Task ConsumeUntilStoppedAsync(CancellationToken stoppingToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(stoppingToken).ConfigureAwait(false);
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken).ConfigureAwait(false);

        try
        {
            // Set QoS prefetch.
            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: _options.PrefetchCount,
                global: false,
                cancellationToken: stoppingToken).ConfigureAwait(false);

            // Declare main exchange.
            await _channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken).ConfigureAwait(false);

            // Declare DLX / DLQ.
            await DeadLetterHandler.DeclareAsync(
                _channel,
                _options.ExchangeName,
                _options.ConsumerName,
                stoppingToken).ConfigureAwait(false);

            // Declare and bind a queue per subscription.
            foreach (var sub in _subscriptions)
            {
                var queueName = $"{_options.ConsumerName}.{sub.EventTypeId}";
                var queueArgs = DeadLetterHandler.GetQueueArguments(_options.ExchangeName);

                await _channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArgs,
                    cancellationToken: stoppingToken).ConfigureAwait(false);

                await _channel.QueueBindAsync(
                    queue: queueName,
                    exchange: _options.ExchangeName,
                    routingKey: sub.EventTypeId,
                    arguments: null,
                    cancellationToken: stoppingToken).ConfigureAwait(false);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += (_, ea) => HandleDeliveryAsync(ea, stoppingToken);

                await _channel.BasicConsumeAsync(
                    queue: queueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken).ConfigureAwait(false);

                Log.Subscribed(_logger, queueName, sub.EventTypeId);
            }

            // Keep running until stopped.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            await _channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            _channel.Dispose();
            _channel = null;
        }
    }

    private async Task HandleDeliveryAsync(BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        var routingKey = ea.RoutingKey;
        var deliveryTag = ea.DeliveryTag;

        // Resolve CLR type from stable event ID — no reflection.
        var clrType = _registry.Resolve(routingKey);
        if (clrType is null)
        {
            Log.UnknownEventType(_logger, routingKey);
            await _channel!.BasicNackAsync(deliveryTag, multiple: false, requeue: false, CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }

        // Read headers.
        var headers = ea.BasicProperties.Headers;
        var messageId = ReadHeaderString(headers, "messageId") ?? ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString();
        var traceParent = ReadHeaderString(headers, "traceparent");
        var traceState = ReadHeaderString(headers, "tracestate");
        var occurredAtRaw = ReadHeaderString(headers, "occurredAt");
        _ = DateTimeOffset.TryParse(occurredAtRaw, out var occurredAt);

        if (!Guid.TryParse(messageId, out var messageGuid))
        {
            messageGuid = Guid.NewGuid();
        }

        var context = new MessageContext
        {
            MessageId = messageGuid,
            EventType = routingKey,
            TraceParent = traceParent,
            TenantId = ReadHeaderString(headers, "tenant_id") ?? ReadHeaderString(headers, "tenantId"),
            OccurredOn = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt,
        };

        // Restore W3C trace context.
        Activity? activity = null;
        if (traceParent is not null &&
            ActivityContext.TryParse(traceParent, traceState, out var activityContext))
        {
            activity = ActivitySource.StartActivity(
                $"consume {routingKey}",
                ActivityKind.Consumer,
                activityContext);
        }

        try
        {
            // Check x-death count for DLX routing.
            var deathCount = GetDeathCount(headers);
            if (deathCount >= _options.MaxDeliveryAttempts)
            {
                Log.ExceededMaxAttempts(_logger, messageGuid, routingKey, _options.MaxDeliveryAttempts);
                await _channel!.BasicNackAsync(deliveryTag, multiple: false, requeue: false, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var tenantContext = scope.ServiceProvider.GetService<ITenantContext>();
            if (tenantContext?.IsMultiTenant == true && !TenantIdValidation.IsValid(context.TenantId))
            {
                // 无租户或非法租户信封不能进入 handler，也不能污染幂等记录。
                await _channel!.BasicNackAsync(deliveryTag, multiple: false, requeue: false, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }
            using var tenantScope = tenantContext?.IsMultiTenant == true
                ? tenantContext.BeginScope(context.TenantId) : null;
            var store = scope.ServiceProvider.GetRequiredService<IProcessedMessageStore>();
            var idempotency = new IdempotentConsumerDecorator(
                store,
                scope.ServiceProvider.GetRequiredService<ILogger<IdempotentConsumerDecorator>>());

            var payload = Encoding.UTF8.GetString(ea.Body.Span);

            await idempotency.ExecuteAsync(
                consumerName: _options.ConsumerName,
                messageId: messageGuid,
                ttl: _options.IdempotencyTtl,
                handler: async ct =>
                {
                    await DispatchAsync(scope.ServiceProvider, clrType, payload, context, ct).ConfigureAwait(false);
                },
                cancellationToken: stoppingToken).ConfigureAwait(false);

            await _channel!.BasicAckAsync(deliveryTag, multiple: false, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.HandlerError(_logger, ex, messageGuid, routingKey);
            await _channel!.BasicNackAsync(deliveryTag, multiple: false, requeue: true, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private static async Task DispatchAsync(
        IServiceProvider services,
        Type clrType,
        string payload,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        // Resolve dispatcher registered per subscription — no MakeGenericType.
        // Dispatchers are singletons; handler resolution uses the scoped services parameter.
        var dispatchers = services.GetServices<ISubscriptionDispatcher>();
        ISubscriptionDispatcher? dispatcher = null;
        foreach (var d in dispatchers)
        {
            if (d.EventClrType == clrType)
            {
                dispatcher = d;
                break;
            }
        }

        if (dispatcher is null)
        {
            throw new InvalidOperationException(
                $"No ISubscriptionDispatcher registered for event type '{clrType.Name}'.");
        }

        await dispatcher.DispatchAsync(clrType, payload, context, services, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadHeaderString(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => value?.ToString(),
        };
    }

    private static int GetDeathCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-death", out var xDeath))
        {
            return 0;
        }

        if (xDeath is not List<object> deaths || deaths.Count == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var entry in deaths)
        {
            if (entry is IDictionary<string, object?> table &&
                table.TryGetValue("count", out var countObj) &&
                countObj is long count)
            {
                total += (int)count;
            }
        }

        return total;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 30, Level = LogLevel.Information,
            Message = "No RabbitMQ subscriptions registered — subscriber is idle.")]
        public static partial void NoSubscriptions(ILogger logger);

        [LoggerMessage(EventId = 31, Level = LogLevel.Information,
            Message = "Subscribed to queue {Queue} (routingKey={RoutingKey})")]
        public static partial void Subscribed(ILogger logger, string queue, string routingKey);

        [LoggerMessage(EventId = 32, Level = LogLevel.Warning,
            Message = "Unknown event type '{RoutingKey}' — nacking without requeue.")]
        public static partial void UnknownEventType(ILogger logger, string routingKey);

        [LoggerMessage(EventId = 33, Level = LogLevel.Warning,
            Message = "Message {MessageId} ({RoutingKey}) exceeded max delivery attempts ({Max}) — routing to DLX.")]
        public static partial void ExceededMaxAttempts(ILogger logger, Guid messageId, string routingKey, int max);

        [LoggerMessage(EventId = 34, Level = LogLevel.Error,
            Message = "Error processing message {MessageId} ({RoutingKey}) — nacking for requeue.")]
        public static partial void HandlerError(ILogger logger, Exception ex, Guid messageId, string routingKey);

        [LoggerMessage(EventId = 35, Level = LogLevel.Warning,
            Message = "Consume session failed — reconnecting in {BackoffSeconds}s.")]
        public static partial void ConsumeSessionFailed(ILogger logger, Exception ex, double backoffSeconds);
    }
}
