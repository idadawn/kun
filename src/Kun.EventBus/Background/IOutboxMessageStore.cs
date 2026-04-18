using Kun.EventBus.Outbox;

namespace Kun.EventBus.Background;

/// <summary>
/// Abstracts access to the outbox persistence layer. The concrete implementation
/// lives in Kun.EntityFrameworkCore (or any other persistence module).
/// </summary>
public interface IOutboxMessageStore
{
    /// <summary>Fetches up to <paramref name="batchSize"/> pending messages ordered by <see cref="OutboxMessage.OccurredAt"/>.</summary>
    Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>Persists the updated state (Status, AttemptCount, PublishedAt, LastError) of a message.</summary>
    Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
