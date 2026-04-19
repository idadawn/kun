using Kun.Core.Domain;

namespace Kun.Sample.Order.Domain.Events;

/// <summary>Raised when an order is cancelled, including the cancellation reason.</summary>
public sealed record OrderCancelledDomainEvent(Guid OrderId, string Reason) : IDomainEvent
{
    /// <inheritdoc/>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
