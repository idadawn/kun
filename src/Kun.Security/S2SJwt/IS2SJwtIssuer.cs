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
}
