#pragma warning disable KUNAUDIT001 // 验证兼容期内的旧事件读取与写入。
using FluentAssertions;
using Kun.EventBus;
using NSubstitute;
using Xunit;

namespace Kun.Audit.Tests;

public sealed class EventBusAuditWriterTests
{
    [Fact]
    public async Task WriteAsync_PublishesAuditLogOnIntegrationEventBus()
    {
        var bus = Substitute.For<IIntegrationEventBus>();
        var writer = new EventBusAuditWriter(bus);
        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-a",
            Action = "agent.created",
            ResourceType = "Agent",
            ResourceId = "42",
            Timestamp = DateTimeOffset.UtcNow,
        };

        await writer.WriteAsync(entry, TestContext.Current.CancellationToken);

        bus.Received(1).Publish(Arg.Is<AuditLog>(x => x.Id == entry.Id && x.Action == "agent.created"));
    }

    [Fact]
    public void EventTypeId_MatchesDocumentedConstant()
    {
        EventBusAuditWriter.EventTypeId.Should().Be("kun.audit.record.v1");
    }
}
