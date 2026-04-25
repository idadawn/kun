using Microsoft.AspNetCore.Builder;

namespace Kun.MultiTenancy.Middleware;

/// <summary>Registration extensions for <see cref="TenantContextMiddleware"/>.</summary>
public static class KunMultiTenancyApplicationBuilderExtensions
{
    /// <summary>
    /// Inserts the Kun multi-tenancy middleware into the pipeline. Call
    /// after <c>UseAuthentication()</c> so the JWT-claim resolver sees
    /// the authenticated <c>HttpContext.User</c>.
    /// </summary>
    public static IApplicationBuilder UseKunMultiTenancy(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<TenantContextMiddleware>();
    }
}
