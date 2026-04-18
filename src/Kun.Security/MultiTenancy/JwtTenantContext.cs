using Kun.Core.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace Kun.Security.MultiTenancy;

/// <summary>
/// <see cref="ITenantContext"/> implementation that extracts <c>tenant_id</c>
/// from the authenticated <see cref="System.Security.Claims.ClaimsPrincipal"/> on the
/// current HTTP request. Falls back gracefully when unauthenticated.
/// </summary>
public sealed class JwtTenantContext : ITenantContext
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
    public string? TenantId =>
        _httpContextAccessor.HttpContext?.User?.FindFirst(TenantIdClaimType)?.Value;

    /// <inheritdoc />
    public bool IsMultiTenant => TenantId is not null;
}
