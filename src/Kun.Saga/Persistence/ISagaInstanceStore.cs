namespace Kun.Saga.Persistence;

/// <summary>
/// Persistence contract for saga instance state and step queue entries.
/// Implementations are responsible for durable storage and querying of saga lifecycle data.
/// </summary>
public interface ISagaInstanceStore
{
    /// <summary>
    /// Finds a saga instance by its unique identifier.
    /// Returns <see langword="null"/> if no matching instance exists.
    /// </summary>
    /// <param name="sagaId">The saga instance identifier.</param>
    /// <param name="ct">Token to observe for cancellation.</param>
    Task<SagaInstance?> FindAsync(Guid sagaId, CancellationToken ct = default);

    /// <summary>Persists a newly created saga instance.</summary>
    /// <param name="instance">The saga instance to add.</param>
    /// <param name="ct">Token to observe for cancellation.</param>
    Task AddAsync(SagaInstance instance, CancellationToken ct = default);

    /// <summary>Persists state changes to an existing saga instance.</summary>
    /// <param name="instance">The saga instance with updated state.</param>
    /// <param name="ct">Token to observe for cancellation.</param>
    Task UpdateAsync(SagaInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Returns saga instances whose <c>TimeoutAt</c> is in the past and whose state is
    /// <see cref="SagaState.Running"/>, up to <paramref name="batchSize"/> results.
    /// Used by <c>SagaTimeoutDetector</c> to trigger compensation.
    /// </summary>
    /// <param name="now">The current UTC time to compare against <c>TimeoutAt</c>.</param>
    /// <param name="batchSize">Maximum number of timed-out instances to return.</param>
    /// <param name="ct">Token to observe for cancellation.</param>
    Task<IReadOnlyList<SagaInstance>> FindTimedOutAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default);

    /// <summary>
    /// Returns pending (unprocessed) step queue entries ordered by <c>EnqueuedAt</c> ascending,
    /// up to <paramref name="batchSize"/> results.
    /// </summary>
    /// <param name="batchSize">Maximum number of entries to return.</param>
    /// <param name="ct">Token to observe for cancellation.</param>
    Task<IReadOnlyList<SagaStepQueueEntry>> FetchPendingStepsAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Persists a newly enqueued step entry.</summary>
    /// <param name="entry">The step queue entry to add.</param>
    /// <param name="ct">Token to observe for cancellation.</param>
    Task EnqueueStepAsync(SagaStepQueueEntry entry, CancellationToken ct = default);

    /// <summary>Persists state changes to an existing step queue entry (e.g. after processing).</summary>
    /// <param name="entry">The step queue entry with updated state.</param>
    /// <param name="ct">Token to observe for cancellation.</param>
    Task UpdateStepAsync(SagaStepQueueEntry entry, CancellationToken ct = default);
}
