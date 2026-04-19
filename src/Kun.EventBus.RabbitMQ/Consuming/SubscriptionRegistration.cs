namespace Kun.EventBus.RabbitMQ.Consuming;

/// <summary>Records a single event-type → handler subscription for the subscriber background service.</summary>
internal sealed class SubscriptionRegistration
{
    /// <summary>Stable event type ID (e.g. <c>order.placed.v1</c>).</summary>
    public required string EventTypeId { get; init; }

    /// <summary>The CLR event type.</summary>
    public required Type EventClrType { get; init; }
}
