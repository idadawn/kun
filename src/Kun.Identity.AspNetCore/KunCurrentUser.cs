using System.Security.Claims;
using Kun.Core.Security;
using Kun.Identity.Abstractions;
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
        => Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Principal?.FindFirstValue("sub");

    /// <inheritdoc />
    public string? TenantId => Principal?.FindFirstValue(TenantIdClaimType);

    /// <inheritdoc />
    public string? Email
        => Principal?.FindFirstValue(ClaimTypes.Email)
            ?? Principal?.FindFirstValue("email");

    /// <inheritdoc />
    public string? Name
        => Principal?.FindFirstValue(ClaimTypes.Name)
            ?? Principal?.FindFirstValue("name");

    /// <inheritdoc />
    public IReadOnlyList<string> Roles
    {
        get
        {
            if (Principal is null)
            {
                return Array.Empty<string>();
            }

            var roles = new List<string>(capacity: 4);
            foreach (var claim in Principal.FindAll(ClaimTypes.Role))
            {
                roles.Add(claim.Value);
            }
            foreach (var claim in Principal.FindAll("roles"))
            {
                roles.Add(claim.Value);
            }
            return roles;
        }
    }
}
