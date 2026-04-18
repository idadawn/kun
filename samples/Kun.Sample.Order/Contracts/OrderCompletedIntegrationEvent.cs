namespace Kun.Sample.Order.Contracts;

/// <summary>
/// Integration event published when an order completes all saga steps successfully.
/// Stable event ID: <c>order.completed.v1</c>.
/// </summary>
public sealed record OrderCompletedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    DateTimeOffset CompletedAt);
