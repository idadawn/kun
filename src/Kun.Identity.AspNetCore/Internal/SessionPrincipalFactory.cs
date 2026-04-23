using System.Security.Claims;
using Kun.Identity.Abstractions.Session;

namespace Kun.Identity.AspNetCore.Internal;

/// <summary>
/// Builds the <see cref="ClaimsPrincipal"/> that the cookie middleware
/// installs on <c>HttpContext.User</c> from a <see cref="SessionData"/>
/// entry. Reflection-free by construction — every claim is created
/// explicitly from a named <see cref="SessionData"/> field.
/// </summary>
internal static class SessionPrincipalFactory
{
    /// <summary>Authentication scheme name emitted on the principal.</summary>
    public const string SchemeName = "KunCookieAuth";

    public static ClaimsPrincipal FromSession(SessionData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var claims = new List<Claim>(capacity: 4 + data.Roles.Count)
        {
            new(ClaimTypes.NameIdentifier, data.UserId),
        };

        if (!string.IsNullOrWhiteSpace(data.TenantId))
        {
            claims.Add(new Claim(KunCurrentUser.TenantIdClaimType, data.TenantId));
        }
        if (!string.IsNullOrWhiteSpace(data.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, data.Email));
        }
        if (!string.IsNullOrWhiteSpace(data.Name))
        {
            claims.Add(new Claim(ClaimTypes.Name, data.Name));
        }
        foreach (var role in data.Roles)
        {
            if (!string.IsNullOrWhiteSpace(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = new ClaimsIdentity(claims, authenticationType: SchemeName);
        return new ClaimsPrincipal(identity);
    }
}
