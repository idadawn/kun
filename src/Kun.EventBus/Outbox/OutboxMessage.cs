namespace Kun.EventBus.Outbox;

/// <summary>
/// Persisted Outbox message row. Created when an integration event is enqueued inside a
/// <c>SaveChangesAsync</c> transaction; published asynchronously by the background publisher.
/// </summary>
/// <remarks>
/// ORM mapping (column names, index, table) lives in
/// <c>Kun.EntityFrameworkCore/OutboxMessageConfiguration.cs</c> — no ORM attributes here.
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Gets the primary key (deterministic GUID supplied by the caller).</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the stable event type identifier, e.g. <c>order.placed.v1</c>.
    /// Never derived from <see cref="System.Type.AssemblyQualifiedName"/>.
    /// </summary>
    public string EventTypeId { get; private set; }

    /// <summary>Gets the JSON-serialized event payload.</summary>
    public string Payload { get; private set; }

    /// <summary>Gets the W3C <c>traceparent</c> header captured at enqueue time; <see langword="null"/> when no activity was active.</summary>
    public string? TraceParent { get; private set; }

    /// <summary>Gets the W3C <c>tracestate</c> header captured at enqueue time; <see langword="null"/> when absent.</summary>
    public string? TraceState { get; private set; }

    /// <summary>Gets the tenant identifier for multi-tenant routing; <see langword="null"/> for single-tenant deployments.</summary>
    public string? TenantId { get; private set; }

    /// <summary>Gets the UTC timestamp at which the producing domain event fired.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Gets the current lifecycle state of this message.</summary>
    public OutboxStatus Status { get; private set; }

    /// <summary>Gets the UTC timestamp at which this message was successfully published; <see langword="null"/> until published.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Gets the number of publish attempts made so far.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Gets the error message from the last failed publish attempt; <see langword="null"/> if none.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Initializes a new <see cref="OutboxMessage"/> with all required fields and sets
    /// <see cref="Status"/> to <see cref="OutboxStatus.Pending"/>.
    /// </summary>
    /// <param name="id">Deterministic GUID for this message row.</param>
    /// <param name="eventTypeId">Stable event type identifier (e.g. <c>order.placed.v1</c>).</param>
    /// <param name="payload">JSON-serialized event payload.</param>
    /// <param name="tenantId">Tenant scope; <see langword="null"/> for single-tenant.</param>
    /// <param name="traceParent">W3C traceparent; <see langword="null"/> when no activity is active.</param>
    /// <param name="traceState">W3C tracestate; <see langword="null"/> when absent.</param>
    /// <param name="occurredAt">UTC timestamp when the producing domain event fired.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="eventTypeId"/> or <paramref name="payload"/> is <see langword="null"/>.
    /// </exception>
    public OutboxMessage(
        Guid id,
        string eventTypeId,
        string payload,
        string? tenantId,
        string? traceParent,
        string? traceState,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(eventTypeId);
        ArgumentNullException.ThrowIfNull(payload);

        Id = id;
        EventTypeId = eventTypeId;
        Payload = payload;
        TenantId = tenantId;
        TraceParent = traceParent;
        TraceState = traceState;
        OccurredAt = occurredAt;
        Status = OutboxStatus.Pending;
        AttemptCount = 0;
    }

    // EF Core parameterless constructor
    private OutboxMessage()
    {
        EventTypeId = string.Empty;
        Payload = string.Empty;
    }

    /// <summary>Transitions the message to <see cref="OutboxStatus.Published"/> and records the publish timestamp.</summary>
    /// <param name="publishedAt">The UTC time at which the message was successfully published.</param>
    public void MarkPublished(DateTimeOffset publishedAt)
    {
        Status = OutboxStatus.Published;
        PublishedAt = publishedAt;
    }

    /// <summary>Transitions the message to <see cref="OutboxStatus.Failed"/> and records the error.</summary>
    /// <param name="error">The error message describing why publishing failed permanently.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public void MarkFailed(string error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Status = OutboxStatus.Failed;
        LastError = error;
    }

    /// <summary>Increments the publish attempt counter by one.</summary>
    public void IncrementAttempt() => AttemptCount++;
}
