namespace Kun.Auditing;

/// <summary>
/// Request-scoped DTO that carries ambient audit context from the outer host
/// (<see cref="Middleware.AuditContextMiddleware"/>) into the EF Core
/// <see cref="AuditingSaveChangesInterceptor"/>.
/// </summary>
/// <remarks>
/// All fields are nullable and default to <see langword="null"/>; the middleware (or any
/// caller) populates them before the command handler opens its unit of work. The
/// interceptor does not fill in defaults — missing fields surface as <see langword="null"/>
/// on the emitted <see cref="Events.AuditEventIntegrationEvent"/>, which is the expected
/// behaviour for background jobs and other hosts without an HTTP context.
/// </remarks>
public sealed class AuditContext
{
    /// <summary>Remote IP address (e.g. <c>HttpContext.Connection.RemoteIpAddress.ToString()</c>).</summary>
    public string? IpAddress { get; set; }

    /// <summary>Client User-Agent header value, when present.</summary>
    public string? UserAgent { get; set; }

    /// <summary>W3C trace id (<c>Activity.Current?.TraceId.ToString()</c>), when an activity is active.</summary>
    public string? TraceId { get; set; }

    /// <summary>Subject identifier of the acting principal (<c>sub</c> claim, user id, service id, …).</summary>
    public string? ActorId { get; set; }

    /// <summary>
    /// Kind of actor performing the action. Defaults to <c>"system"</c> for background-job /
    /// uninitialized contexts; typical values are <c>"user"</c>, <c>"agent"</c>, <c>"system"</c>.
    /// </summary>
    public string ActorType { get; set; } = "system";
}
