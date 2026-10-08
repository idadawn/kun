using System.Text.Json;
using Kun.Auditing.DependencyInjection;
using Kun.Auditing.Events;
using Kun.EventBus;
using Kun.EventBus.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kun.Auditing.Tests;

public sealed class AuditEventRegistrationTests
{
    [Fact]
    public void RegisterAuditing_PreservesBothEventVersionsAndStringSubjects()
    {
        var services = new ServiceCollection();
        services.AddKunEventBus(builder => builder.RegisterAuditing());
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IEventTypeRegistry>();
        Assert.Equal(typeof(AuditEventIntegrationEvent), registry.Resolve("kun.audit.event.v1"));
        Assert.Equal(typeof(AuditEventIntegrationEventV2), registry.Resolve("kun.audit.event.v2"));
        Assert.NotNull(registry.GetJsonTypeInfo(typeof(AuditEventIntegrationEvent)));
        var info = registry.GetJsonTypeInfo(typeof(AuditEventIntegrationEventV2));
        Assert.NotNull(info);
        var original = new AuditEventIntegrationEventV2(Guid.CreateVersion7(), "tenant-a", "oidc-sub", "agent",
            "agent-1", "Added", "Order", "order-1", null, null, DateTimeOffset.UtcNow);
        var payload = JsonSerializer.Serialize(original, info);
        Assert.Equal(original, JsonSerializer.Deserialize(payload, info));
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("tenant-a", document.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal("oidc-sub", document.RootElement.GetProperty("userId").GetString());
    }
}
