using Kun.Core.Domain;

namespace Kun.Sample.Catalog.Domain.Events;

/// <summary>Raised when a new <see cref="Product"/> is created.</summary>
public sealed record ProductCreatedDomainEvent(
    Guid ProductId,
    string Name,
    decimal Price,
    Guid CategoryId) : IDomainEvent
{
    /// <inheritdoc/>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
