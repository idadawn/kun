using Kun.EventBus.Outbox;
using Xunit;

namespace Kun.EventBus.Tests;

public sealed class OutboxMessageTests
{
    private sealed record OrderPlaced(Guid OrderId);

    [Fact]
    public void OutboxMessage_SetsFieldsCorrectly()
    {
        var id = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var payload = $"{{\"orderId\":\"{orderId}\"}}";
        var occurredAt = DateTimeOffset.UtcNow;

        var msg = new OutboxMessage(
            id,
            "order.placed.v1",
            payload,
            tenantId: null,
            traceParent: null,
            traceState: null,
            occurredAt);

        Assert.Equal(id, msg.Id);
        Assert.Equal("order.placed.v1", msg.EventTypeId);
        Assert.Equal(payload, msg.Payload);
        Assert.Equal(OutboxStatus.Pending, msg.Status);
        Assert.Equal(occurredAt, msg.OccurredAt);
        Assert.Equal(0, msg.AttemptCount);
    }

    [Fact]
    public void OutboxMessage_WithTraceParent_Propagates()
    {
        var msg = new OutboxMessage(
            Guid.NewGuid(),
            "order.placed.v1",
            "{}",
            tenantId: null,
            traceParent: "00-abc-def-01",
            traceState: null,
            DateTimeOffset.UtcNow);

        Assert.Equal("00-abc-def-01", msg.TraceParent);
    }

    [Fact]
    public void OutboxMessage_MarkPublished_SetsStatus()
    {
        var msg = new OutboxMessage(Guid.NewGuid(), "order.placed.v1", "{}", null, null, null, DateTimeOffset.UtcNow);
        var publishedAt = DateTimeOffset.UtcNow;
        msg.MarkPublished(publishedAt);

        Assert.Equal(OutboxStatus.Published, msg.Status);
        Assert.Equal(publishedAt, msg.PublishedAt);
    }

    [Fact]
    public void OutboxMessage_MarkFailed_SetsStatus()
    {
        var msg = new OutboxMessage(Guid.NewGuid(), "order.placed.v1", "{}", null, null, null, DateTimeOffset.UtcNow);
        msg.MarkFailed("broker unreachable");

        Assert.Equal(OutboxStatus.Failed, msg.Status);
        Assert.Equal("broker unreachable", msg.LastError);
    }

    [Fact]
    public void Buffer_AddAndGet_RoundTrips()
    {
        var buffer = new IntegrationEventBuffer();
        var msg = new OutboxMessage(Guid.NewGuid(), "order.placed.v1", "{}", null, null, null, DateTimeOffset.UtcNow);
        buffer.Add(msg);

        Assert.Single(buffer.GetPendingMessages());
        Assert.Equal(msg.Id, buffer.GetPendingMessages()[0].Id);
    }

    [Fact]
    public void Buffer_Clear_EmptiesMessages()
    {
        var buffer = new IntegrationEventBuffer();
        buffer.Add(new OutboxMessage(Guid.NewGuid(), "order.placed.v1", "{}", null, null, null, DateTimeOffset.UtcNow));
        buffer.Clear();

        Assert.Empty(buffer.GetPendingMessages());
    }
}
