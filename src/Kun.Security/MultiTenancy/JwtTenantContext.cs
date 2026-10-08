using Kun.Core.MultiTenancy;
using Kun.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Kun.Security.MultiTenancy;

/// <summary>
/// <see cref="ITenantContext"/> implementation that extracts <c>tenant_id</c>
/// from the authenticated <see cref="System.Security.Claims.ClaimsPrincipal"/> on the
/// current HTTP request. Falls back gracefully when unauthenticated.
/// </summary>
public sealed class JwtTenantContext : ITenantContext, ITenantIdSource
{
    private const string TenantIdClaimType = "tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes the context with an HTTP context accessor.</summary>
    /// <param name="httpContextAccessor">Accessor for the current HTTP context.</param>
    public JwtTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string? TenantId
    {
        get
        {
            var claims = _httpContextAccessor.HttpContext?.User.Identities
                .Where(identity => identity.IsAuthenticated)
                .SelectMany(identity => identity.FindAll(TenantIdClaimType)).ToArray();
            return claims is { Length: 1 } && TenantIdValidation.IsValid(claims[0].Value)
                ? claims[0].Value : null;
        }
    }

    /// <inheritdoc />
    public bool IsMultiTenant => TenantId is not null;
}
