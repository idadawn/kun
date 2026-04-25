namespace Kun.Audit;

/// <summary>
/// Pluggable write target for audit entries. Implementations decide where
/// entries land (DB table, append-only log file, remote audit service) and
/// how durable the write is. The pipeline behaviour surrounds calls to this
/// interface with the per-tenant chain-state lock, so implementations may
/// treat writes as already serialised within a tenant.
/// </summary>
public interface IAuditSink
{
    /// <summary>Persists a single audit entry.</summary>
    /// <param name="entry">The entry to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
