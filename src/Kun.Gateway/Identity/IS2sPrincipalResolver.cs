using System.Security.Claims;

namespace Kun.Gateway.Identity;

/// <summary>
/// Optional enrichment hook invoked by <see cref="S2sTokenIssuer"/> after the external IdP
/// principal authenticates and before the internal S2S token is minted. Lets consumers add,
/// remove, or rewrite claims — typically for tenant lookup, role projection, or attaching
/// service-scoped metadata that the IdP does not own.
/// </summary>
/// <remarks>
/// The resolver runs on the Gateway's request path, so its implementation should be cheap:
/// prefer cache-backed lookups over per-request database calls. Failures should surface as
/// exceptions rather than empty principals. The issuer rejects unauthenticated/empty principals
/// and missing canonical identity fields; session and authorization revisions must come from an authoritative source.
/// </remarks>
public interface IS2sPrincipalResolver
{
    /// <summary>
    /// Returns the principal that <see cref="S2sTokenIssuer.IssueFor(ClaimsPrincipal)"/> should
    /// translate into the internal S2S token. Implementations may return
    /// <paramref name="externalPrincipal"/> unchanged (no-op) or a new
    /// <see cref="ClaimsPrincipal"/> with enriched claims.
    /// </summary>
    /// <param name="externalPrincipal">The authenticated principal from the IdP JWT.</param>
    /// <param name="cancellationToken">Cancellation token for the enrichment call.</param>
    /// <returns>The principal to mint the token for.</returns>
    Task<ClaimsPrincipal> ResolveAsync(ClaimsPrincipal externalPrincipal, CancellationToken cancellationToken);
}
