using System.Collections.Concurrent;

namespace Kun.Audit;

/// <summary>
/// Process-local <see cref="IAuditChainState"/>. Serialises read-compute-write
/// per tenant with a <see cref="SemaphoreSlim"/>. Suitable for tests and
/// single-instance deployments; multi-instance deployments should back the
/// chain state with a durable store and DB-level uniqueness so two replicas
/// cannot fork the chain.
/// </summary>
public sealed class InMemoryAuditChainState : IAuditChainState, IDisposable
{
    private const string GlobalKey = "__global__";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _lastHash = new(StringComparer.Ordinal);
    private int _disposed;

    /// <inheritdoc />
    public async ValueTask<string> AppendAsync(
        string? tenantKey,
        Func<string?, ValueTask<string>> computeNewHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computeNewHash);
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        var key = tenantKey ?? GlobalKey;
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lastHash.TryGetValue(key, out var prev);
            var next = await computeNewHash(prev).ConfigureAwait(false);
            ArgumentException.ThrowIfNullOrWhiteSpace(next, nameof(computeNewHash));
            _lastHash[key] = next;
            return next;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>Returns the current chain tip for <paramref name="tenantKey"/>, or <see langword="null"/> if the chain is empty.</summary>
    public string? PeekTip(string? tenantKey)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        var key = tenantKey ?? GlobalKey;
        return _lastHash.TryGetValue(key, out var tip) ? tip : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        foreach (var sem in _locks.Values)
        {
            sem.Dispose();
        }

        _locks.Clear();
    }
}
