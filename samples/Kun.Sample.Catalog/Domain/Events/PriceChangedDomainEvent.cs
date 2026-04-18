using Kun.Core.Domain;

namespace Kun.Sample.Catalog.Domain.Events;

/// <summary>Raised when a <see cref="Product"/>'s price is changed.</summary>
public sealed record PriceChangedDomainEvent(
    Guid ProductId,
    decimal OldPrice,
    decimal NewPrice) : IDomainEvent
{
    /// <inheritdoc/>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
