namespace Kun.Security.S2SJwt;

/// <summary>
/// Issues S2S JWT tokens signed with ES256 for internal service-to-service authentication.
/// </summary>
public interface IS2SJwtIssuer
{
    /// <summary>
    /// Issues a new S2S JWT token matching the SAD v3.0 §12 claim contract.
    /// </summary>
    /// <param name="request">The subject, tenant, audience, and optional claim inputs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The encoded JWT string.</returns>
    Task<string> IssueAsync(S2SJwtIssueRequest request, CancellationToken ct = default);

    /// <summary>
    /// Convenience overload that composes a <see cref="S2SJwtIssueRequest"/> from the
    /// commonly used five fields and delegates to <see cref="IssueAsync(S2SJwtIssueRequest, CancellationToken)"/>.
    /// </summary>
    /// <param name="userId">Subject identifier (JWT <c>sub</c>).</param>
    /// <param name="tenantId">Tenant identifier claim.</param>
    /// <param name="scopes">Scope tokens joined with spaces into the <c>scope</c> claim.</param>
    /// <param name="audience">Token audience list (<c>aud</c>).</param>
    /// <param name="sessionId">Originating session identifier (<c>session_id</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> IssueAsync(
        string userId,
        string tenantId,
        string[] scopes,
        string[] audience,
        string sessionId,
        CancellationToken ct = default);
}
