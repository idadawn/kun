using Kun.EventBus;

namespace Kun.Sample.Order.Contracts;

/// <summary>
/// Integration event published when an order completes all saga steps successfully.
/// </summary>
[IntegrationEventId("order.completed.v1")]
public sealed record OrderCompletedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    DateTimeOffset CompletedAt);
