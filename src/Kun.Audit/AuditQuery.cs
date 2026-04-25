namespace Kun.Audit;

/// <summary>
/// Filter + pagination parameters for <see cref="Abstractions.IAuditReader.QueryAsync"/>.
/// Filters combine with <c>AND</c>; unset filters match anything.
/// </summary>
/// <param name="TenantId">Restrict to a single tenant. Required — audit reads are tenant-scoped by convention and a <see langword="null"/> value would cross tenant boundaries.</param>
/// <param name="ActorId">Restrict to one actor's actions. Optional.</param>
/// <param name="Action">Restrict to a single stable action string (e.g. <c>agent.created</c>). Optional.</param>
/// <param name="ResourceType">Restrict to one resource type name. Optional.</param>
/// <param name="ResourceId">Restrict to a single resource primary key. Optional.</param>
/// <param name="From">Inclusive lower bound on <see cref="AuditLog.Timestamp"/>.</param>
/// <param name="To">Exclusive upper bound on <see cref="AuditLog.Timestamp"/>.</param>
/// <param name="Limit">Maximum number of rows to yield (default 100; implementations should cap at 1000 to avoid accidental table scans).</param>
public sealed record AuditQuery(
    string TenantId,
    string? ActorId = null,
    string? Action = null,
    string? ResourceType = null,
    string? ResourceId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Limit = 100);
