namespace Kun.Sample.Catalog.Contracts;

/// <summary>
/// Integration event published when a new product is made available in the catalog.
/// Stable event ID: <c>product.published.v1</c>
/// </summary>
public sealed record ProductPublishedIntegrationEvent(
    Guid ProductId,
    string Name,
    decimal Price)
{
    /// <summary>Unique message ID for idempotent consumers.</summary>
    public Guid EventId { get; } = Guid.NewGuid();

    /// <summary>UTC timestamp when this event was produced.</summary>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
