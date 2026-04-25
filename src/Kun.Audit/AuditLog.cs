using Kun.Core.Domain;

namespace Kun.Audit;

/// <summary>
/// Canonical audit-log record emitted by the framework on business-relevant
/// state changes (aggregate create / update / delete, explicit
/// <see cref="Abstractions.AuditableAttribute"/> invocations, login events, …).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AuditLog"/> is a persistence-agnostic POCO. ORM mapping (table
/// name, column types, indexes, immutability triggers) lives in
/// <c>Kun.Audit.EntityFrameworkCore/AuditLogConfiguration.cs</c> — no EF /
/// JSON attributes are placed here so the type stays Core-layer friendly.
/// </para>
/// <para>
/// Implements <see cref="IMultiTenant"/> so that when consumers store audit
/// rows inside a <c>KunDbContext</c>-derived context, the automatic tenant
/// global query filter isolates tenant A's audits from tenant B's without
/// extra configuration. Consumers that run audits on a dedicated database
/// can ignore this interface.
/// </para>
/// </remarks>
public sealed class AuditLog : IMultiTenant
{
    /// <summary>Surrogate primary key; deterministic GUID assigned by the writer.</summary>
    public Guid Id { get; init; }

    /// <summary>Tenant scope of the audited change; <see langword="null"/> for platform-level events (e.g. boot-time system actions).</summary>
    public string? TenantId { get; init; }

    /// <summary>Identity of the actor that caused the change, extracted from <c>ICurrentUser.UserId</c>; <see langword="null"/> for unattributed system actions.</summary>
    public string? ActorId { get; init; }

    /// <summary>Category of the actor — one of the <see cref="AuditActorType"/> constants.</summary>
    public string ActorType { get; init; } = AuditActorType.Unknown;

    /// <summary>
    /// Stable verb identifying what happened, e.g. <c>agent.created</c>,
    /// <c>artifact.uploaded</c>, <c>user.login.succeeded</c>. Business code
    /// supplies this through <see cref="Abstractions.AuditableAttribute"/> or, for
    /// interceptor-driven audits, it is derived from the EF
    /// <c>EntityState</c> (<c>{resourceType}.created</c>,
    /// <c>{resourceType}.updated</c>, <c>{resourceType}.deleted</c>).
    /// </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>CLR or business type-name of the affected resource (typically the aggregate type name, e.g. <c>Agent</c>).</summary>
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>Primary-key value of the affected resource, rendered as string for homogeneity across Guid / long / string keys; <see langword="null"/> when the change isn't tied to a single row.</summary>
    public string? ResourceId { get; init; }

    /// <summary>W3C trace id at the time of the change (extracted from <see cref="System.Diagnostics.Activity.Current"/>); enables cross-referencing audits with distributed traces.</summary>
    public string? TraceId { get; init; }

    /// <summary>UTC timestamp of the audit event.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Optional JSON-encoded payload snapshot. Interceptor-driven audits
    /// serialize the set of changed properties; attribute-driven audits may
    /// pass the Command DTO. May be <see langword="null"/> when
    /// <c>KunAuditOptions.CaptureEntityPayload</c> is disabled or when the
    /// change contains PII that the caller chose to redact.
    /// </summary>
    public string? PayloadJson { get; init; }

    /// <summary>Remote client address, when an ambient HTTP context exposes one; <see langword="null"/> for background-worker originated audits.</summary>
    public string? IpAddress { get; init; }
}
