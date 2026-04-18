using RabbitMQ.Client;
using Kun.EventBus.RabbitMQ.Options;

namespace Kun.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Declares the dead-letter exchange (DLX) and dead-letter queue (DLQ) for a consumer.
/// When a message exceeds <see cref="RabbitMqOptions.MaxDeliveryAttempts"/>, it is published
/// to the DLX with the original routing key and headers preserved.
/// </summary>
internal static class DeadLetterHandler
{
    /// <summary>Gets the DLX exchange name for the given base exchange.</summary>
    public static string GetDlxName(string exchangeName) => $"{exchangeName}.dlx";

    /// <summary>Gets the DLQ queue name for the given consumer.</summary>
    public static string GetDlqName(string consumerName) => $"{consumerName}.dead-letter";

    /// <summary>
    /// Declares the DLX (fanout) and DLQ and binds them. Must be called before consuming starts.
    /// </summary>
    public static async Task DeclareAsync(
        IChannel channel,
        string exchangeName,
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        var dlxName = GetDlxName(exchangeName);
        var dlqName = GetDlqName(consumerName);

        await channel.ExchangeDeclareAsync(
            exchange: dlxName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queue: dlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueBindAsync(
            queue: dlqName,
            exchange: dlxName,
            routingKey: "#",
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the queue arguments that configure a work-queue to route rejected messages to the DLX.
    /// </summary>
    public static Dictionary<string, object?> GetQueueArguments(string exchangeName) =>
        new()
        {
            ["x-dead-letter-exchange"] = GetDlxName(exchangeName),
        };
}
