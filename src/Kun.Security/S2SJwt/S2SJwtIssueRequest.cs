namespace Kun.Security.S2SJwt;

/// <summary>
/// Input for <see cref="IS2SJwtIssuer.IssueAsync"/>. Carries the full SAD v3.0 §12
/// NeuroAgent S2S claim set.
/// </summary>
/// <param name="UserId">User subject (UUID). Emitted as the <c>sub</c> claim.</param>
/// <param name="TenantId">Tenant identifier (UUID). Emitted as <c>tenant_id</c>.</param>
/// <param name="Audience">
/// Target audience(s). A single entry becomes a string <c>aud</c>; multiple entries are
/// emitted as an <c>aud</c> array in the payload.
/// </param>
/// <param name="Roles">
/// Roles granted to the caller. Each role is emitted as a distinct <c>roles</c> claim
/// (per SAD §12, which models roles as a multi-valued claim rather than a space-joined scope).
/// </param>
/// <param name="Scopes">
/// OAuth2-style permission scopes. Joined with a single space and emitted as one
/// <c>scope</c> claim. Use this when the consumer expects an OAuth2-compatible scope string;
/// prefer <paramref name="Roles"/> for RBAC semantics.
/// </param>
/// <param name="SessionId">Session identifier for revocation support. Emitted as <c>session_id</c> when set.</param>
/// <param name="MembershipId">
/// Current membership id (when a user has multiple memberships within a tenant).
/// Emitted as <c>membership_id</c> when set.
/// </param>
/// <param name="TraceId">
/// W3C trace id (hex) for OpenTelemetry trace continuity across the service mesh.
/// Emitted as <c>trace_id</c> when set.
/// </param>
/// <param name="Lifetime">
/// Optional per-call override for token lifetime. When <see langword="null"/>,
/// <see cref="S2SJwtOptions.DefaultLifetimeSeconds"/> applies.
/// </param>
public sealed record S2SJwtIssueRequest(
    string UserId,
    string TenantId,
    IReadOnlyList<string> Audience,
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<string>? Scopes = null,
    string? SessionId = null,
    string? MembershipId = null,
    string? TraceId = null,
    TimeSpan? Lifetime = null);
