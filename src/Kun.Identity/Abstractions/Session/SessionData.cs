namespace Kun.Identity.Abstractions.Session;

/// <summary>
/// Serialized payload held by <see cref="ISessionStore"/> implementations.
/// The session cookie carries only an opaque session id; the record
/// below is what that id resolves to.
/// </summary>
/// <param name="UserId">
/// Stable user identifier. Mirrors the <c>sub</c> claim from the OIDC
/// id-token / access-token. Required — sessions without a user are not
/// authenticated.
/// </param>
/// <param name="TenantId">
/// Active tenant identifier resolved from the <c>tenant_id</c> claim, or
/// <see langword="null"/> when the consumer is single-tenant.
/// </param>
/// <param name="Email">Email claim, when present on the id-token.</param>
/// <param name="Name">Display name claim, when present on the id-token.</param>
/// <param name="Roles">
/// Flattened role list from <c>roles</c> / <c>groups</c> claim. Empty
/// when no role claim was issued.
/// </param>
/// <param name="AccessToken">
/// OIDC access token. Persisted so downstream API calls made on the
/// user's behalf (e.g. userinfo refresh) can reuse it without
/// re-running the code-exchange flow.
/// </param>
/// <param name="RefreshToken">
/// OIDC refresh token when the IdP issued one. Stored server-side only
/// — never round-tripped through the cookie.
/// </param>
/// <param name="ExpiresAt">
/// Absolute instant at which <see cref="AccessToken"/> stops being
/// valid. When this is sooner than the session TTL, background refresh
/// is responsible for rotating the access token; session expiry itself
/// is driven by the cookie / store TTL set at sign-in.
/// </param>
public sealed record SessionData(
    string UserId,
    string? TenantId,
    string? Email,
    string? Name,
    IReadOnlyList<string> Roles,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt);
