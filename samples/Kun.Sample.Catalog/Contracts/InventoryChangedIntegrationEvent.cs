namespace Kun.Sample.Catalog.Contracts;

/// <summary>
/// Integration event published when a product's stock changes.
/// Stable event ID: <c>inventory.changed.v1</c>
/// </summary>
public sealed record InventoryChangedIntegrationEvent(
    Guid ProductId,
    int StockDelta,
    int NewStock)
{
    /// <summary>Unique message ID for idempotent consumers.</summary>
    public Guid EventId { get; } = Guid.NewGuid();

    /// <summary>UTC timestamp when this event was produced.</summary>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
