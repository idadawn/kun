using Kun.Core.Domain;

namespace Kun.Sample.Order.Domain.Events;

/// <summary>Raised when a new order is placed (status = Pending).</summary>
public sealed record OrderPlacedDomainEvent(Guid OrderId, Guid CustomerId, decimal TotalAmount) : IDomainEvent
{
    /// <inheritdoc/>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
