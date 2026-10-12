using System.Security.Claims;
using Kun.Core.Security;
using Kun.Identity.Abstractions;
using Kun.Security.Authentication;
using Microsoft.AspNetCore.Http;

namespace Kun.Identity.AspNetCore;

/// <summary>
/// Request-scoped <see cref="IAuthenticatedUser"/> (and
/// <see cref="ICurrentUser"/>) resolver. Reads claims off
/// <c>HttpContext.User</c> — which <c>UseKunCookieAuth</c> populates
/// from <c>ISessionStore</c> earlier in the pipeline. Anonymous when
/// no <see cref="IHttpContextAccessor"/> is available or the
/// authentication middleware has not run.
/// </summary>
public sealed class KunCurrentUser : IAuthenticatedUser
{
    /// <summary>
    /// Claim type used for tenant id. The cookie middleware populates
    /// this from <c>SessionData.TenantId</c>; the S2S path populates
    /// it from the signed <c>tenant_id</c> claim on the internal JWT.
    /// </summary>
    public const string TenantIdClaimType = "tenant_id";

    private readonly IHttpContextAccessor _accessor;

    /// <summary>Creates a resolver bound to the supplied HTTP context accessor.</summary>
    public KunCurrentUser(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <inheritdoc />
    public ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public string? UserId
        => PrincipalClaims.TryRead(Principal, out var subject, out _, out _) ? subject : null;

    /// <inheritdoc />
    public string PrincipalKind => PrincipalClaims.TryRead(Principal, out _, out _, out var kind) ? kind : string.Empty;

    private ClaimsIdentity? AuthenticatedIdentity => Principal?.Identity is ClaimsIdentity { IsAuthenticated: true } identity ? identity : null;

    /// <inheritdoc />
    public string? TenantId => PrincipalClaims.TryRead(Principal, out _, out var tenant, out _) ? tenant : null;

    /// <inheritdoc />
    public string? Email
        => AuthenticatedIdentity?.FindFirst(ClaimTypes.Email)?.Value
            ?? AuthenticatedIdentity?.FindFirst("email")?.Value;

    /// <inheritdoc />
    public string? Name
        => AuthenticatedIdentity?.FindFirst(ClaimTypes.Name)?.Value
            ?? AuthenticatedIdentity?.FindFirst("name")?.Value;

    /// <inheritdoc />
    public IReadOnlyList<string> Roles
    {
        get
        {
            if (AuthenticatedIdentity is null)
            {
                return Array.Empty<string>();
            }

            var roles = new List<string>(capacity: 4);
            foreach (var claim in AuthenticatedIdentity.FindAll(ClaimTypes.Role))
            {
                roles.Add(claim.Value);
            }
            foreach (var claim in AuthenticatedIdentity.FindAll("roles"))
            {
                roles.Add(claim.Value);
            }
            return roles;
        }
    }
}
