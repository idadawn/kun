using System.Security.Claims;
using Kun.Core.Security;
using Kun.Security.Authentication;
using Microsoft.AspNetCore.Http;

namespace Kun.Security.Security;

/// <summary>
/// <see cref="ICurrentUser"/> implementation that reads <c>sub</c>, <c>name</c>,
/// and <c>roles</c> claims from the authenticated <see cref="ClaimsPrincipal"/>
/// on the current HTTP request.
/// Also implements <see cref="ITracedCurrentUser"/> to expose the <c>trace_id</c> claim
/// propagated from the gateway S2S JWT for OpenTelemetry trace continuity.
/// </summary>
public sealed class JwtCurrentUser : ICurrentUser, ITracedCurrentUser
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
    public string? UserId => PrincipalClaims.TryRead(Principal, out var subject, out _, out _) ? subject : null;

    /// <inheritdoc />
    public string PrincipalKind => PrincipalClaims.TryRead(Principal, out _, out _, out var kind) ? kind : string.Empty;

    private ClaimsIdentity? AuthenticatedIdentity => Principal?.Identity is ClaimsIdentity { IsAuthenticated: true } identity ? identity : null;

    /// <inheritdoc />
    public IReadOnlyList<string> Roles =>
        AuthenticatedIdentity?.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type is "role" or "roles")
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
        ?? [];

    /// <inheritdoc />
    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public string? TraceId =>
        AuthenticatedIdentity?.FindFirst("trace_id")?.Value;
}
