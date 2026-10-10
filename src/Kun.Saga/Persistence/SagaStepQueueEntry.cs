namespace Kun.Saga.Persistence;

/// <summary>
/// A queued work item representing a single step execution or compensation for a saga.
/// The background <c>SagaStepExecutor</c> polls this table and processes entries in order.
/// ORM mapping lives in <c>SagaStepQueueEntryConfiguration</c> — no ORM attributes here.
/// </summary>
public sealed class SagaStepQueueEntry
{
    /// <summary>Gets the unique identifier for this queue entry.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the saga instance this entry belongs to.</summary>
    public Guid SagaId { get; private set; }

    /// <summary>Gets the zero-based index of the step to execute or compensate.</summary>
    public int StepIndex { get; private set; }

    /// <summary>Gets the direction: forward <see cref="StepDirection.Execute"/> or reverse <see cref="StepDirection.Compensate"/>.</summary>
    public StepDirection Direction { get; private set; }

    /// <summary>Gets the UTC timestamp when this entry was added to the queue.</summary>
    public DateTimeOffset EnqueuedAt { get; private set; }

    /// <summary>Gets the UTC timestamp when this entry was successfully processed; <see langword="null"/> while pending.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Earliest UTC time for the next attempt; null means immediately eligible.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    internal void ScheduleRetry(DateTimeOffset nextAttemptAt) => NextAttemptAt = nextAttemptAt;

    /// <summary>Gets the number of execution attempts made for this entry.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// Creates a new <see cref="SagaStepQueueEntry"/> with zero attempts and no processed timestamp.
    /// </summary>
    /// <param name="id">Unique identifier for this entry.</param>
    /// <param name="sagaId">The owning saga instance identifier.</param>
    /// <param name="stepIndex">Zero-based step index.</param>
    /// <param name="direction">Forward execution or compensation.</param>
    /// <param name="enqueuedAt">UTC timestamp of enqueue.</param>
    public SagaStepQueueEntry(
        Guid id,
        Guid sagaId,
        int stepIndex,
        StepDirection direction,
        DateTimeOffset enqueuedAt)
    {
        Id = id;
        SagaId = sagaId;
        StepIndex = stepIndex;
        Direction = direction;
        EnqueuedAt = enqueuedAt;
        AttemptCount = 0;
    }

    // EF Core parameterless constructor.
    private SagaStepQueueEntry() { }

    /// <summary>Records that this entry has been successfully processed at <paramref name="processedAt"/>.</summary>
    /// <param name="processedAt">UTC timestamp of successful processing.</param>
    public void MarkProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
    }

    /// <summary>Increments the attempt counter by one. Called before each execution attempt.</summary>
    public void IncrementAttempt() => AttemptCount++;
}
