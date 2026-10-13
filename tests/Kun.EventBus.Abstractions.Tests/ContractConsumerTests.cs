using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Kun.Core.Domain;
using Xunit;

namespace Kun.EventBus.Abstractions.Tests;

public sealed class ContractConsumerTests
{
    [Fact]
    public void ContractOnlyConsumer_WithSourceGeneratedJson_ShouldRoundTripEvent()
    {
        var expected = new CustomerCreated(Guid.NewGuid(), DateTime.UtcNow, "customer-1");

        var json = JsonSerializer.Serialize(expected, EventJsonContext.Default.CustomerCreated);
        var actual = JsonSerializer.Deserialize(json, EventJsonContext.Default.CustomerCreated);

        actual.Should().Be(expected);
        ((IIntegrationEvent)actual!).EventId.Should().Be(expected.EventId);
        new IntegrationEventIdAttribute("customer.created.v1").EventType.Should().Be("customer.created.v1");
    }

    [Fact]
    public void ContractAssembly_ReferencedLibraries_ShouldContainOnlyCoreAndBcl()
    {
        var assembly = typeof(IEventTypeRegistrar).Assembly;

        assembly.GetName().Name.Should().Be("Kun.EventBus.Abstractions");
        assembly.GetReferencedAssemblies().Select(item => item.Name)
            .Should().OnlyContain(name => name == "Kun.Core" || name!.StartsWith("System.", StringComparison.Ordinal));
        assembly.GetForwardedTypes().Should().Contain(typeof(IIntegrationEvent));
        typeof(IIntegrationEvent).Assembly.GetName().Name.Should().Be("Kun.Core");
    }

    [Fact]
    public void RegistrationPort_WithJsonTypeInfo_ShouldBeUsableByContractOnlyConsumer()
    {
        var registrar = new RecordingRegistrar();
        IEventTypeRegistrar port = registrar;

        var returned = port.Register("customer.created.v1", EventJsonContext.Default.CustomerCreated);

        returned.Should().BeSameAs(port);
        registrar.EventId.Should().Be("customer.created.v1");
        registrar.TypeInfo.Should().BeSameAs(EventJsonContext.Default.CustomerCreated);
    }

    private sealed class RecordingRegistrar : IEventTypeRegistrar
    {
        public string? EventId { get; private set; }
        public System.Text.Json.Serialization.Metadata.JsonTypeInfo? TypeInfo { get; private set; }

        public IEventTypeRegistrar Register<TEvent>(string eventType) where TEvent : class =>
            throw new NotSupportedException();

        public IEventTypeRegistrar Register<TEvent>(string eventType,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<TEvent> typeInfo) where TEvent : class
        {
            EventId = eventType;
            TypeInfo = typeInfo;
            return this;
        }

        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Attribute-based registration is not used by this consumer.")]
        public IEventTypeRegistrar Register<TEvent>() where TEvent : class =>
            throw new NotSupportedException();
    }
}

[IntegrationEventId("customer.created.v1")]
public sealed record CustomerCreated(Guid EventId, DateTime OccurredAt, string CustomerId) : IIntegrationEvent;

[JsonSerializable(typeof(CustomerCreated))]
internal sealed partial class EventJsonContext : JsonSerializerContext;
