namespace Kun.EventBus.Outbox;

/// <summary>In-memory accumulator for outbox messages within a single unit-of-work scope.</summary>
public sealed class IntegrationEventBuffer : IIntegrationEventOutbox
{
    private readonly List<OutboxMessage> _messages = [];

    /// <inheritdoc/>
    public void Add(OutboxMessage message) => _messages.Add(message);

    /// <inheritdoc/>
    public IReadOnlyList<OutboxMessage> GetPendingMessages() => _messages.AsReadOnly();

    /// <summary>Clears staged messages after they have been flushed to the store.</summary>
    public void Clear() => _messages.Clear();
}
