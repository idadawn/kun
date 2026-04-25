namespace Kun.Audit.Abstractions;

/// <summary>Queries the audit log store. Tenant-scoped by contract — see <see cref="AuditQuery.TenantId"/>.</summary>
public interface IAuditReader
{
    /// <summary>Yields audit entries matching <paramref name="query"/>, newest first.</summary>
    /// <param name="query">Filter + pagination parameters.</param>
    /// <param name="cancellationToken">Cancels the async enumeration.</param>
    /// <returns>An async stream of matching audit logs.</returns>
    IAsyncEnumerable<AuditLog> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}
