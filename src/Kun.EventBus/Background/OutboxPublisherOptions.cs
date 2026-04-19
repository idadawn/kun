namespace Kun.EventBus.Background;

/// <summary>Configures the outbox publisher background service.</summary>
public sealed class OutboxPublisherOptions
{
    /// <summary>Maximum messages fetched per poll. Default: 50.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Poll interval between outbox scans. Default: 2 seconds.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Max retry attempts before a message is transitioned to <see cref="Outbox.OutboxStatus.Failed"/>. Default: 5.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Initial retry delay (exponential backoff base). Default: 1 second.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);
}
