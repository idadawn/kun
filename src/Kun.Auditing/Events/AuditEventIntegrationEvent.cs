using System.Text.Json;

namespace Kun.Auditing.Events;

/// <summary>
/// Integration event emitted by <see cref="AuditingSaveChangesInterceptor"/> for every
/// tracked mutation of an entity registered via <c>services.AddAuditable&lt;TEntity&gt;()</c>.
/// Published through the Outbox with stable id <c>kun.audit.event.v1</c>.
/// </summary>
/// <param name="TenantId">Tenant scope; <see langword="null"/> for non-multi-tenant hosts.</param>
/// <param name="UserId">
/// User identifier when the acting principal is a human user; <see langword="null"/>
/// for system / agent actors. When both <paramref name="UserId"/> and <paramref name="ActorId"/>
/// are populated for a user-type actor they should carry the same value.
/// </param>
/// <param name="ActorType">Kind of actor: <c>"user"</c>, <c>"agent"</c>, or <c>"system"</c>.</param>
/// <param name="ActorId">Subject identifier of the actor (claim <c>sub</c>, service id, …).</param>
/// <param name="Action">
/// Domain-level action identifier, e.g. <c>order.placed</c> or the EF
/// <c>EntityState</c> (<c>Added</c> / <c>Modified</c> / <c>Deleted</c>) when no richer
/// action is in scope.
/// </param>
/// <param name="ResourceType">CLR type name of the audited entity (e.g. <c>Order</c>).</param>
/// <param name="ResourceId">Primary key of the audited entity as a string.</param>
/// <param name="Outcome">One of <c>"success"</c>, <c>"denied"</c>, <c>"error"</c>.</param>
/// <param name="TraceId">W3C trace id captured from <c>Activity.Current</c>.</param>
/// <param name="Metadata">
/// Optional caller-supplied metadata. Persisted as raw JSON on the outbox payload. Kept
/// as <see cref="JsonDocument"/> so the shape is opaque to the framework — consumers
/// decide how to parse it.
/// </param>
/// <param name="IpAddress">Remote IP of the originating request, when available.</param>
/// <param name="UserAgent">User-Agent header of the originating request, when available.</param>
/// <param name="CreatedAt">UTC timestamp at which the audit event was produced.</param>
public sealed record AuditEventIntegrationEvent(
    Guid? TenantId,
    Guid? UserId,
    string ActorType,
    string? ActorId,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string Outcome,
    string? TraceId,
    JsonDocument? Metadata,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt);
