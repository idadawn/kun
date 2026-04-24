using System.Security.Claims;
using Kun.Core.Security;

namespace Kun.Identity.Abstractions;

/// <summary>
/// Browser-flow extension of <see cref="ICurrentUser"/>. Adds the
/// multi-tenant identifier, profile claims, and direct access to the
/// ambient <see cref="ClaimsPrincipal"/> that the
/// <c>Kun.Identity.AspNetCore</c> cookie middleware populates.
/// </summary>
/// <remarks>
/// <para>
/// Services that resolve the user from an S2S JWT should keep
/// injecting <see cref="ICurrentUser"/>. Endpoints that serve the
/// browser (cookie + OIDC code flow) can inject
/// <see cref="IAuthenticatedUser"/> to reach the cookie-only fields
/// without casting.
/// </para>
/// <para>
/// The DI registration provided by <c>Kun.Identity.AspNetCore</c>
/// binds both interfaces to the same request-scoped instance, so the
/// choice of injected interface is purely about the fields the caller
/// needs.
/// </para>
/// <para>
/// See <c>docs/architecture/when-to-use-currentuser.md</c> for the
/// full decision table across consumer scenarios and the anti-patterns
/// that should never show up in a PR (no <c>ICurrentUserEx</c>, no
/// per-service subinterface, no <see cref="ClaimsPrincipal"/>
/// injection as a first-class service).
/// </para>
/// </remarks>
public interface IAuthenticatedUser : ICurrentUser
{
    /// <summary>Active tenant identifier, or <see langword="null"/> in single-tenant deployments.</summary>
    string? TenantId { get; }

    /// <summary>Email claim from the id-token, when present.</summary>
    string? Email { get; }

    /// <summary>Display-name claim from the id-token, when present.</summary>
    string? Name { get; }

    /// <summary>
    /// The raw <see cref="ClaimsPrincipal"/> the cookie middleware
    /// installed on <c>HttpContext.User</c>. <see langword="null"/>
    /// when the request is anonymous.
    /// </summary>
    ClaimsPrincipal? Principal { get; }
}
