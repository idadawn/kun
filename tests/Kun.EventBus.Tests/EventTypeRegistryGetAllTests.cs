using Xunit;

namespace Kun.EventBus.Tests;

public sealed class EventTypeRegistryGetAllTests
{
    [Fact]
    public void Empty_Registry_Returns_Empty_Collection()
    {
        var registry = new EventTypeRegistry(new Dictionary<string, Type>());

        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void Populated_Registry_Returns_All_Pairs()
    {
        var registry = new EventTypeRegistry(new Dictionary<string, Type>
        {
            ["order.placed.v1"] = typeof(OrderPlaced),
            ["inventory.adjusted.v1"] = typeof(InventoryAdjusted),
        });

        var all = registry.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(("order.placed.v1", typeof(OrderPlaced)), all);
        Assert.Contains(("inventory.adjusted.v1", typeof(InventoryAdjusted)), all);
    }

    [Fact]
    public void GetAll_Ordering_Is_Stable_And_Alphabetical()
    {
        // Insertion order intentionally reversed to prove the registry reorders.
        var registry = new EventTypeRegistry(new Dictionary<string, Type>
        {
            ["z.last.v1"] = typeof(OrderPlaced),
            ["a.first.v1"] = typeof(InventoryAdjusted),
            ["m.middle.v1"] = typeof(UserSignedUp),
        });

        var ids = registry.GetAll().Select(p => p.EventId).ToArray();

        Assert.Equal(["a.first.v1", "m.middle.v1", "z.last.v1"], ids);
    }

    [Fact]
    public void GetAll_Is_Consistent_With_Resolve_And_GetEventType()
    {
        var registry = new EventTypeRegistry(new Dictionary<string, Type>
        {
            ["order.placed.v1"] = typeof(OrderPlaced),
        });

        var all = registry.GetAll();
        var pair = all.Single();

        Assert.Equal(pair.ClrType, registry.Resolve(pair.EventId));
        Assert.Equal(pair.EventId, registry.GetEventType(pair.ClrType));
    }

    private sealed record OrderPlaced(Guid OrderId);
    private sealed record InventoryAdjusted(Guid ProductId, int Delta);
    private sealed record UserSignedUp(string UserId);
}
