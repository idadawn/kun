using Kun.Core.MultiTenancy;

namespace Kun.MultiTenancy;

/// <summary>
/// <see cref="ITenantContext"/> implementation backed by an
/// <see cref="AsyncLocal{T}"/> so the tenant id flows with the async
/// call graph across thread hops, task schedulers, and concurrent
/// parallel tasks. Pair with <c>TenantContextMiddleware</c> (or a
/// background-worker scope that opens a tenant scope explicitly) to
/// populate the value.
/// </summary>
public sealed class AsyncLocalTenantContext : ITenantContext
{
    private readonly AsyncLocal<string?> _current = new();

    /// <inheritdoc />
    public string? TenantId => _current.Value;

    /// <inheritdoc />
    public bool IsMultiTenant => true;

    /// <summary>
    /// Sets <see cref="TenantId"/> to <paramref name="tenantId"/> for the
    /// returned scope. The previous value is restored on dispose so
    /// nested scopes compose predictably.
    /// </summary>
    public IDisposable BeginScope(string? tenantId)
    {
        var previous = _current.Value;
        _current.Value = tenantId;
        return new Scope(this, previous);
    }

    private sealed class Scope(AsyncLocalTenantContext owner, string? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._current.Value = previous;
            }
        }
    }
}
