using System.Collections.Concurrent;

namespace Kun.Audit;

/// <summary>
/// Process-local <see cref="IAuditSink"/> that keeps all written entries in
/// memory. Intended for unit tests and non-durable development; do not use in
/// production because entries are lost on process restart and there is no
/// durability guarantee if the process crashes mid-write.
/// </summary>
public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    /// <summary>Snapshot of all entries written so far, in write order.</summary>
    public IReadOnlyCollection<AuditEntry> Entries => _entries.ToArray();

    /// <inheritdoc />
    public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Enqueue(entry);
        return ValueTask.CompletedTask;
    }

    /// <summary>Clears all recorded entries (test helper).</summary>
    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { /* drain */ }
    }
}
