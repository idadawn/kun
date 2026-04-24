namespace Kun.Audit;

/// <summary>
/// Maintains the rolling tip of the per-tenant audit hash chain. The interface
/// exposes a single atomic "acquire-previous, compute-new, commit-new" operation
/// so implementations are free to centralise the lock, the durable read, and the
/// write in one transaction.
/// </summary>
public interface IAuditChainState
{
    /// <summary>
    /// Appends one entry to the tenant's chain. Within <paramref name="computeNewHash"/>
    /// the caller should build the <see cref="AuditEntry"/>, call
    /// <see cref="AuditContentHasher.Compute"/> with the supplied previous hash, and
    /// return the freshly computed hash. The implementation guarantees the
    /// read-compute-write sequence is serialised per <paramref name="tenantKey"/>
    /// so concurrent commands on the same tenant do not fork the chain.
    /// </summary>
    /// <param name="tenantKey">Tenant identifier, or <see langword="null"/> for the shared/global chain.</param>
    /// <param name="computeNewHash">
    /// Factory that, given the previous hash (or <see langword="null"/> when the chain is empty),
    /// produces the new <see cref="AuditEntry.ContentHash"/>. May perform the sink write.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new content hash, now stored as the chain tip.</returns>
    ValueTask<string> AppendAsync(
        string? tenantKey,
        Func<string?, ValueTask<string>> computeNewHash,
        CancellationToken cancellationToken = default);
}
