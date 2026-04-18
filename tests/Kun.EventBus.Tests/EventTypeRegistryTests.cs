using Xunit;

namespace Kun.EventBus.Tests;

public sealed class EventTypeRegistryTests
{
    private sealed record OrderPlaced(Guid OrderId);

    [Fact]
    public void Resolve_KnownToken_ReturnsType()
    {
        var registry = Build("order.placed.v1", typeof(OrderPlaced));
        Assert.Equal(typeof(OrderPlaced), registry.Resolve("order.placed.v1"));
    }

    [Fact]
    public void Resolve_CaseInsensitive()
    {
        var registry = Build("Order.Placed.V1", typeof(OrderPlaced));
        Assert.Equal(typeof(OrderPlaced), registry.Resolve("order.placed.v1"));
    }

    [Fact]
    public void Resolve_UnknownToken_ReturnsNull()
    {
        var registry = Build("order.placed.v1", typeof(OrderPlaced));
        Assert.Null(registry.Resolve("unknown.event.v1"));
    }

    [Fact]
    public void GetEventType_KnownType_ReturnsToken()
    {
        var registry = Build("order.placed.v1", typeof(OrderPlaced));
        Assert.Equal("order.placed.v1", registry.GetEventType(typeof(OrderPlaced)));
    }

    [Fact]
    public void GetEventType_UnknownType_ReturnsNull()
    {
        var registry = Build("order.placed.v1", typeof(OrderPlaced));
        Assert.Null(registry.GetEventType(typeof(string)));
    }

    private static EventTypeRegistry Build(string token, Type type) =>
        new(new Dictionary<string, Type> { [token] = type });
}
