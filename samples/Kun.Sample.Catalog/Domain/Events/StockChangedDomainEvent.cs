using Kun.Core.Domain;

namespace Kun.Sample.Catalog.Domain.Events;

/// <summary>Raised when a <see cref="Product"/>'s stock is adjusted.</summary>
public sealed record StockChangedDomainEvent(
    Guid ProductId,
    int Delta) : IDomainEvent
{
    /// <inheritdoc/>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
