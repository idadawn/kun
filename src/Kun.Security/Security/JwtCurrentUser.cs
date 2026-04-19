using System.Security.Claims;
using Kun.Core.Security;
using Microsoft.AspNetCore.Http;

namespace Kun.Security.Security;

/// <summary>
/// <see cref="ICurrentUser"/> implementation that reads <c>sub</c>, <c>name</c>,
/// and <c>roles</c> claims from the authenticated <see cref="ClaimsPrincipal"/>
/// on the current HTTP request.
/// </summary>
public sealed class JwtCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initializes the user context with an HTTP context accessor.</summary>
    /// <param name="httpContextAccessor">Accessor for the current HTTP context.</param>
    public JwtCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal =>
        _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public string? UserId =>
        Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? Principal?.FindFirst("sub")?.Value;

    /// <inheritdoc />
    public IReadOnlyList<string> Roles =>
        Principal?.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .ToArray()
        ?? [];

    /// <inheritdoc />
    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true;
}
