using Kun.EventBus;

namespace Kun.Sample.Catalog.Contracts;

/// <summary>
/// Integration event published when a product's stock changes.
/// </summary>
[IntegrationEventId("inventory.changed.v1")]
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
