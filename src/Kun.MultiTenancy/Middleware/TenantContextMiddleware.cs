using Kun.MultiTenancy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Kun.MultiTenancy.Middleware;

/// <summary>
/// Resolves the inbound request's tenant id and opens an
/// <see cref="AsyncLocalTenantContext"/> scope that bounds every
/// async continuation downstream from this middleware.
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware bound to <paramref name="next"/>.</summary>
    public TenantContextMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    /// <summary>Resolves the tenant, scopes it, and invokes the next middleware.</summary>
    public async Task InvokeAsync(
        HttpContext httpContext,
        ITenantResolver resolver,
        AsyncLocalTenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tenantContext);

        var tenantId = await resolver.ResolveTenantIdAsync(httpContext).ConfigureAwait(false);
        using (tenantContext.BeginScope(tenantId))
        {
            await _next(httpContext).ConfigureAwait(false);
        }
    }
}
