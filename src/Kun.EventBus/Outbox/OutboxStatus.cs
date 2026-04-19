namespace Kun.EventBus.Outbox;

/// <summary>Lifecycle state of a persisted Outbox message.</summary>
public enum OutboxStatus
{
    /// <summary>Persisted but not yet published to the message bus.</summary>
    Pending = 0,

    /// <summary>Successfully published.</summary>
    Published = 1,

    /// <summary>Publish permanently failed after exhausting retries.</summary>
    Failed = 2,
}
