using Kun.MultiTenancy.Abstractions;
using Kun.MultiTenancy.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        if (tenantId is null && httpContext.RequestServices
            .GetService<IOptions<KunMultiTenancyOptions>>()?.Value.Production == true)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        using (tenantContext.BeginScope(tenantId))
        {
            await _next(httpContext).ConfigureAwait(false);
        }
    }
}
