namespace Kun.Security.S2SJwt;

/// <summary>
/// Issues S2S JWT tokens signed with ES256 for internal service-to-service authentication.
/// </summary>
public interface IS2SJwtIssuer
{
    /// <summary>
    /// Issues a new S2S JWT token.
    /// </summary>
    /// <param name="userId">User subject claim (UUID).</param>
    /// <param name="tenantId">Tenant identifier (UUID).</param>
    /// <param name="scopes">Space-separated OAuth2-style scopes.</param>
    /// <param name="audience">Target audience(s) for the token.</param>
    /// <param name="sessionId">Session identifier for revocation support.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The encoded JWT string.</returns>
    Task<string> IssueAsync(string userId, string tenantId, string[] scopes, string[] audience, string sessionId, CancellationToken ct = default);
}
