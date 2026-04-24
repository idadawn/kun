namespace Kun.Audit;

/// <summary>
/// Immutable record describing one audited command execution. Each entry is
/// part of a per-tenant hash chain: <see cref="ContentHash"/> covers this entry
/// plus the <see cref="PrevHash"/> of the previous entry in the same chain,
/// so retroactive tampering with any row invalidates every following row.
/// </summary>
/// <param name="Id">Unique identifier for this audit row.</param>
/// <param name="TenantId">Tenant scope, <see langword="null"/> when acting outside a tenant.</param>
/// <param name="ActorId">User / service principal that invoked the command, <see langword="null"/> when anonymous.</param>
/// <param name="CommandName">Short name of the command type (e.g. <c>CreateOrderCommand</c>).</param>
/// <param name="OccurredAt">UTC timestamp captured before the handler ran.</param>
/// <param name="LatencyMs">Wall-clock duration of the handler invocation, in milliseconds.</param>
/// <param name="Outcome">Whether the handler completed normally or threw.</param>
/// <param name="ErrorCode">
/// Exception type name on <see cref="AuditOutcome.Exception"/>, otherwise <see langword="null"/>.
/// Business-level Result.Error codes are out of scope for the MVP and can be layered on later.
/// </param>
/// <param name="PrevHash">Content hash of the previous entry in the same tenant's chain; <see langword="null"/> for the first entry.</param>
/// <param name="ContentHash">Lower-case hex SHA-256 chain hash produced by <see cref="AuditContentHasher.Compute"/>.</param>
public sealed record AuditEntry(
    Guid Id,
    string? TenantId,
    string? ActorId,
    string CommandName,
    DateTimeOffset OccurredAt,
    long LatencyMs,
    AuditOutcome Outcome,
    string? ErrorCode,
    string? PrevHash,
    string ContentHash);
