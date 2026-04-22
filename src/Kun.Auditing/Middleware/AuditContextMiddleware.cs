using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Kun.Auditing.Middleware;

/// <summary>
/// ASP.NET Core middleware that populates the request-scoped <see cref="AuditContext"/>
/// with data captured from the incoming <see cref="HttpContext"/>: remote IP,
/// User-Agent header, and the current W3C trace id.
/// </summary>
/// <remarks>
/// <para>
/// Registered via <c>services.AddKunAuditing()</c> as scoped, then mounted into the
/// pipeline with <c>app.UseMiddleware&lt;AuditContextMiddleware&gt;()</c> before any
/// command / unit-of-work that must emit audit events.
/// </para>
/// <para>
/// The middleware only captures transport-level metadata. Actor identity
/// (<see cref="AuditContext.ActorId"/> / <see cref="AuditContext.ActorType"/>) is populated
/// downstream — e.g. by a <c>JwtCurrentUser</c> bridge — because those values depend on
/// the service-to-service auth pipeline which runs after request routing.
/// </para>
/// </remarks>
public sealed class AuditContextMiddleware : IMiddleware
{
    private readonly AuditContext _auditContext;

    /// <summary>Creates a new <see cref="AuditContextMiddleware"/>.</summary>
    /// <param name="auditContext">The request-scoped audit context to populate.</param>
    public AuditContextMiddleware(AuditContext auditContext)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        _auditContext = auditContext;
    }

    /// <inheritdoc />
    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        _auditContext.IpAddress = context.Connection.RemoteIpAddress?.ToString();
        _auditContext.UserAgent = context.Request.Headers.UserAgent.ToString();
        _auditContext.TraceId = Activity.Current?.TraceId.ToString();

        return next(context);
    }
}
