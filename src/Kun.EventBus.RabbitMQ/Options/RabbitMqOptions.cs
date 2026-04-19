namespace Kun.EventBus.RabbitMQ.Options;

/// <summary>Configures the RabbitMQ transport for Kun event bus.</summary>
public sealed class RabbitMqOptions
{
    /// <summary>AMQP URI, e.g. <c>amqp://guest:guest@localhost:5672/</c>.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Topic exchange that publishers write to and consumers bind queues to.</summary>
    public string ExchangeName { get; init; } = "kun.events";

    /// <summary>Logical consumer name (used as queue name prefix and idempotency scope).</summary>
    public required string ConsumerName { get; init; }

    /// <summary>Prefetch count per channel.</summary>
    public ushort PrefetchCount { get; init; } = 32;

    /// <summary>Max redeliveries before a message is routed to the dead-letter exchange. Default 5.</summary>
    public int MaxDeliveryAttempts { get; init; } = 5;

    /// <summary>Idempotency key TTL. Default 30 minutes.</summary>
    public TimeSpan IdempotencyTtl { get; init; } = TimeSpan.FromMinutes(30);
}
