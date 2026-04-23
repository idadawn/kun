namespace Kun.Audit.Abstractions;

/// <summary>
/// Persists or publishes an <see cref="AuditLog"/> entry.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation in this assembly is
/// <see cref="NullAuditWriter"/> (drops entries), so interceptor wiring
/// is safe even when no audit backend has been configured. Production
/// deployments register one of:
/// </para>
/// <list type="bullet">
///   <item><c>EfAuditWriter</c> (from <c>Kun.Audit.EntityFrameworkCore</c>) — synchronous DB insert inside the business transaction.</item>
///   <item><see cref="EventBusAuditWriter"/> — stages the entry on the Outbox as a <c>kun.audit.record.v1</c> integration event.</item>
///   <item>Custom implementations (SIEM forwarder, file-append, …).</item>
/// </list>
/// <para>
/// Implementations should be idempotent with respect to <see cref="AuditLog.Id"/>:
/// interceptor re-entry (EF Core retries, transient failures followed by
/// Outbox replay) must not duplicate audit rows.
/// </para>
/// </remarks>
public interface IAuditWriter
{
    /// <summary>Persists or publishes the <paramref name="entry"/>.</summary>
    /// <param name="entry">The audit event to record.</param>
    /// <param name="cancellationToken">Cancels the write operation.</param>
    Task WriteAsync(AuditLog entry, CancellationToken cancellationToken = default);
}
