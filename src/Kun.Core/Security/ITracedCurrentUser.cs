namespace Kun.Core.Security;

/// <summary>
/// Extension of <see cref="ICurrentUser"/> that exposes the ambient OpenTelemetry trace id.
/// Cast or inject this sub-interface when trace-continuity access is required.
/// </summary>
public interface ITracedCurrentUser : ICurrentUser
{
    /// <summary>
    /// The OpenTelemetry trace id propagated via the <c>trace_id</c> claim on the S2S JWT,
    /// or <c>null</c> when the token was issued without trace context (e.g. background refresh).
    /// </summary>
    string? TraceId { get; }
}
