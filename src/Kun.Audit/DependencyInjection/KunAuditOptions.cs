namespace Kun.Audit.DependencyInjection;

/// <summary>Configuration for <c>AddKunAudit</c>.</summary>
public sealed class KunAuditOptions
{
    /// <summary>
    /// When <see langword="true"/> (default), the EF Core interceptor
    /// captures the set of changed property values as
    /// <see cref="AuditLog.PayloadJson"/>. Disable in deployments where
    /// the payload would routinely contain PII that the consumer has no
    /// redaction strategy for.
    /// </summary>
    public bool CaptureEntityPayload { get; set; } = true;

    /// <summary>
    /// Actor type used when <c>ICurrentUser</c> is authenticated but the
    /// claim set does not otherwise indicate a service-to-service call.
    /// Defaults to <see cref="AuditActorType.User"/>.
    /// </summary>
    public string DefaultActorType { get; set; } = AuditActorType.User;

    /// <summary>
    /// Actor type used when <c>ICurrentUser.IsAuthenticated</c> is
    /// <see langword="false"/> (background workers, migrations, scheduled
    /// jobs). Defaults to <see cref="AuditActorType.System"/>.
    /// </summary>
    public string AnonymousActorType { get; set; } = AuditActorType.System;

    /// <summary>
    /// When <see langword="true"/> (default), the interceptor swallows
    /// writer exceptions and logs them at <c>Error</c> level instead of
    /// letting them roll back the business transaction. Set to
    /// <see langword="false"/> for compliance-critical deployments where
    /// no business change may commit without a successful audit write.
    /// </summary>
    public bool SwallowWriterExceptions { get; set; } = true;
}
