namespace Kun.Saga.Persistence;

/// <summary>
/// Durable state for a running or completed saga instance.
/// ORM mapping lives in <c>SagaInstanceConfiguration</c> — no ORM attributes here.
/// </summary>
public sealed class SagaInstance
{
    /// <summary>Gets the unique saga instance identifier (equals <c>ISagaContext.SagaId</c>).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the stable saga type name, e.g. <c>OrderCreationSaga</c>.</summary>
    public string SagaName { get; private set; }

    /// <summary>Gets the tenant identifier; <see langword="null"/> for single-tenant deployments.</summary>
    public string? TenantId { get; private set; }

    /// <summary>Gets the JSON-serialized saga context payload used to resume execution.</summary>
    public string ContextPayload { get; private set; }

    /// <summary>Gets the zero-based index of the step currently being executed or compensated.</summary>
    public int CurrentStepIndex { get; private set; }

    /// <summary>Gets the current lifecycle state of this saga instance.</summary>
    public SagaState State { get; private set; }

    /// <summary>Gets the UTC timestamp when this saga was started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets the UTC timestamp of the most recent state or step update.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the current step will time out and trigger compensation;
    /// <see langword="null"/> when no timeout is configured.
    /// </summary>
    public DateTimeOffset? TimeoutAt { get; private set; }

    /// <summary>Gets the UTC timestamp when this saga reached a terminal state; <see langword="null"/> while running.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Gets the reason string captured on failure; <see langword="null"/> while running.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Gets the W3C <c>traceparent</c> captured at saga start; used for distributed trace correlation.</summary>
    public string? TraceId { get; private set; }

    /// <summary>
    /// Creates a new <see cref="SagaInstance"/> in the <see cref="SagaState.Running"/> state at step 0.
    /// </summary>
    /// <param name="id">Unique saga instance identifier.</param>
    /// <param name="sagaName">Stable saga type name.</param>
    /// <param name="tenantId">Tenant identifier; <see langword="null"/> for single-tenant.</param>
    /// <param name="contextPayload">JSON-serialized context.</param>
    /// <param name="startedAt">UTC timestamp of saga start.</param>
    /// <param name="timeoutAt">Optional UTC timestamp for step timeout.</param>
    /// <param name="traceId">W3C traceparent for trace correlation.</param>
    public SagaInstance(
        Guid id,
        string sagaName,
        string? tenantId,
        string contextPayload,
        DateTimeOffset startedAt,
        DateTimeOffset? timeoutAt,
        string? traceId)
    {
        ArgumentNullException.ThrowIfNull(sagaName);
        ArgumentNullException.ThrowIfNull(contextPayload);

        Id = id;
        SagaName = sagaName;
        TenantId = tenantId;
        ContextPayload = contextPayload;
        CurrentStepIndex = 0;
        State = SagaState.Running;
        StartedAt = startedAt;
        UpdatedAt = startedAt;
        TimeoutAt = timeoutAt;
        TraceId = traceId;
    }

    // EF Core parameterless constructor.
    private SagaInstance()
    {
        SagaName = string.Empty;
        ContextPayload = string.Empty;
    }

    /// <summary>
    /// Updates <see cref="ContextPayload"/> and advances <see cref="CurrentStepIndex"/>
    /// to <paramref name="stepIndex"/>. Also resets <see cref="TimeoutAt"/> for the new step.
    /// </summary>
    /// <param name="stepIndex">The next step index to execute.</param>
    /// <param name="updatedAt">UTC timestamp of this update.</param>
    /// <param name="newTimeoutAt">New timeout deadline for the upcoming step.</param>
    /// <param name="contextPayload">Updated JSON-serialized context.</param>
    /// <exception cref="InvalidOperationException">Thrown if the saga is not in <see cref="SagaState.Running"/>.</exception>
    public void MoveToStep(int stepIndex, DateTimeOffset updatedAt, DateTimeOffset? newTimeoutAt, string contextPayload)
    {
        if (State != SagaState.Running)
        {
            throw new InvalidOperationException($"Cannot move to step {stepIndex}: saga is in state {State}.");
        }

        ArgumentNullException.ThrowIfNull(contextPayload);
        CurrentStepIndex = stepIndex;
        ContextPayload = contextPayload;
        UpdatedAt = updatedAt;
        TimeoutAt = newTimeoutAt;
    }

    /// <summary>
    /// Transitions the saga to <see cref="SagaState.Completed"/> and records the terminal timestamp.
    /// </summary>
    /// <param name="completedAt">UTC timestamp of completion.</param>
    /// <exception cref="InvalidOperationException">Thrown if the saga is not in <see cref="SagaState.Running"/>.</exception>
    public void MarkCompleted(DateTimeOffset completedAt)
    {
        if (State != SagaState.Running)
        {
            throw new InvalidOperationException($"Cannot complete saga: current state is {State}.");
        }

        State = SagaState.Completed;
        CompletedAt = completedAt;
        UpdatedAt = completedAt;
        TimeoutAt = null;
    }

    /// <summary>
    /// Transitions the saga from <see cref="SagaState.Running"/> to <see cref="SagaState.Compensating"/>,
    /// recording the failure reason and setting <see cref="CurrentStepIndex"/> to the step that failed.
    /// </summary>
    /// <param name="failedStepIndex">The index of the step that triggered compensation.</param>
    /// <param name="failureReason">Human-readable description of the failure.</param>
    /// <param name="updatedAt">UTC timestamp of this transition.</param>
    /// <exception cref="InvalidOperationException">Thrown if the saga is not in <see cref="SagaState.Running"/>.</exception>
    public void StartCompensating(int failedStepIndex, string failureReason, DateTimeOffset updatedAt)
    {
        if (State != SagaState.Running)
        {
            throw new InvalidOperationException($"Cannot start compensation: saga is in state {State}.");
        }

        ArgumentNullException.ThrowIfNull(failureReason);
        State = SagaState.Compensating;
        CurrentStepIndex = failedStepIndex;
        FailureReason = failureReason;
        UpdatedAt = updatedAt;
        TimeoutAt = null;
    }

    /// <summary>
    /// Decrements <see cref="CurrentStepIndex"/> during compensation. When the index reaches
    /// -1 (all steps compensated), call <see cref="MarkCompensated"/> instead.
    /// </summary>
    /// <param name="nextCompensationIndex">The next step index to compensate (one less than current).</param>
    /// <param name="updatedAt">UTC timestamp of this update.</param>
    /// <exception cref="InvalidOperationException">Thrown if the saga is not in <see cref="SagaState.Compensating"/>.</exception>
    public void MoveCompensationToStep(int nextCompensationIndex, DateTimeOffset updatedAt)
    {
        if (State != SagaState.Compensating)
        {
            throw new InvalidOperationException($"Cannot move compensation pointer: saga is in state {State}.");
        }

        CurrentStepIndex = nextCompensationIndex;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Transitions the saga to <see cref="SagaState.Compensated"/> once all compensation
    /// steps have completed successfully.
    /// </summary>
    /// <param name="compensatedAt">UTC timestamp of compensation completion.</param>
    /// <exception cref="InvalidOperationException">Thrown if the saga is not in <see cref="SagaState.Compensating"/>.</exception>
    public void MarkCompensated(DateTimeOffset compensatedAt)
    {
        if (State != SagaState.Compensating)
        {
            throw new InvalidOperationException($"Cannot mark compensated: saga is in state {State}.");
        }

        State = SagaState.Compensated;
        CompletedAt = compensatedAt;
        UpdatedAt = compensatedAt;
    }

    /// <summary>
    /// Transitions the saga to <see cref="SagaState.Failed"/> when an unrecoverable error
    /// occurs (e.g. compensation itself fails). Manual intervention is required.
    /// </summary>
    /// <param name="reason">Description of the terminal failure.</param>
    /// <param name="updatedAt">UTC timestamp of this transition.</param>
    public void Fail(string reason, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(reason);
        State = SagaState.Failed;
        FailureReason = reason;
        UpdatedAt = updatedAt;
        TimeoutAt = null;
    }
}
