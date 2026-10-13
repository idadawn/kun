using Xunit;

namespace Kun.EventBus.Tests;

public sealed class ContractForwardingTests
{
    [Theory]
    [InlineData("Kun.EventBus.IntegrationEventIdAttribute", typeof(IntegrationEventIdAttribute))]
    [InlineData("Kun.EventBus.IEventTypeRegistrar", typeof(IEventTypeRegistrar))]
    [InlineData("Kun.EventBus.IEventTypeRegistry", typeof(IEventTypeRegistry))]
    public void LegacyAssemblyReference_WithMovedContract_ShouldResolveSameType(string typeName, Type expected)
    {
        var resolved = Type.GetType($"{typeName}, Kun.EventBus", throwOnError: true);

        Assert.Same(expected, resolved);
        Assert.Equal("Kun.EventBus.Abstractions", resolved!.Assembly.GetName().Name);
    }
}
