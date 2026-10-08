namespace Kun.EventBus;

/// <summary>Metadata accompanying an integration event during handler dispatch.</summary>
public sealed class MessageContext
{
    /// <summary>来自消息信封的租户标识，在解析 handler 前回灌到租户作用域。</summary>
    public string? TenantId { get; init; }

    /// <summary>Unique message identifier from the outbox envelope.</summary>
    public required Guid MessageId { get; init; }

    /// <summary>Stable event type token.</summary>
    public required string EventType { get; init; }

    /// <summary>W3C TraceParent from the originating span, if available.</summary>
    public string? TraceParent { get; init; }

    /// <summary>UTC timestamp when the message was originally enqueued.</summary>
    public required DateTimeOffset OccurredOn { get; init; }
}
