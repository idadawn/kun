using Kun.EventBus.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.EventBus.Tests;

public sealed class IntegrationEventIdAttributeTests
{
    [IntegrationEventId("sample.fired.v1")]
    private sealed record DecoratedEvent;

    private sealed record UndecoratedEvent;

    [Fact]
    public void Attribute_ExposesConfiguredToken()
    {
        var attr = (IntegrationEventIdAttribute?)Attribute.GetCustomAttribute(
            typeof(DecoratedEvent), typeof(IntegrationEventIdAttribute));

        Assert.NotNull(attr);
        Assert.Equal("sample.fired.v1", attr!.EventType);
    }

    [Fact]
    public void Attribute_RejectsNullEventType()
    {
        Assert.Throws<ArgumentNullException>(() => new IntegrationEventIdAttribute(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Attribute_RejectsEmptyOrWhitespaceEventType(string bad)
    {
        Assert.Throws<ArgumentException>(() => new IntegrationEventIdAttribute(bad));
    }

    [Fact]
    public void Register_Generic_WithAttribute_UsesAttributeToken()
    {
        var builder = new KunEventBusBuilder(new ServiceCollection());

        builder.Register<DecoratedEvent>();

        Assert.True(builder.EventTypeMap.ContainsKey("sample.fired.v1"));
        Assert.Equal(typeof(DecoratedEvent), builder.EventTypeMap["sample.fired.v1"]);
    }

    [Fact]
    public void Register_Generic_WithoutAttribute_Throws_WithActionableMessage()
    {
        var builder = new KunEventBusBuilder(new ServiceCollection());

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Register<UndecoratedEvent>());
        Assert.Contains("IntegrationEventId", ex.Message);
    }

    [Fact]
    public void Register_Generic_And_Explicit_String_Produce_Same_Mapping()
    {
        var byAttribute = new KunEventBusBuilder(new ServiceCollection());
        byAttribute.Register<DecoratedEvent>();

        var byString = new KunEventBusBuilder(new ServiceCollection());
        byString.Register<DecoratedEvent>("sample.fired.v1");

        Assert.Equal(byAttribute.EventTypeMap, byString.EventTypeMap);
    }

    [Fact]
    public void Register_Generic_Chains_FluentBuilder()
    {
        var builder = new KunEventBusBuilder(new ServiceCollection());

        var returned = builder.Register<DecoratedEvent>();

        Assert.Same(builder, returned);
    }
}
