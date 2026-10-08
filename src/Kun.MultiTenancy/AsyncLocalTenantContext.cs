using Kun.Core.MultiTenancy;
using Kun.MultiTenancy.Abstractions;

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
    private readonly AsyncLocal<TenantScope?> _current = new();
    private readonly ITenantIdSource? _source;

    /// <summary>创建仅使用显式作用域的上下文。</summary>
    public AsyncLocalTenantContext() { }

    /// <summary>创建可读取已认证请求租户的上下文；显式作用域始终优先。</summary>
    public AsyncLocalTenantContext(ITenantIdSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <inheritdoc />
    public string? TenantId => _current.Value is { } scope ? scope.TenantId : _source?.TenantId;

    /// <inheritdoc />
    public bool IsMultiTenant => true;

    /// <summary>
    /// Sets <see cref="TenantId"/> to <paramref name="tenantId"/> for the
    /// returned scope. The previous value is restored on dispose so
    /// nested scopes compose predictably.
    /// </summary>
    public IDisposable BeginScope(string? tenantId)
    {
        if (tenantId is not null && !TenantIdValidation.IsValid(tenantId))
        {
            throw new ArgumentException("Invalid tenant identifier.", nameof(tenantId));
        }
        var previous = _current.Value;
        _current.Value = new TenantScope(tenantId);
        return new Scope(this, previous);
    }

    private sealed record TenantScope(string? TenantId);

    private sealed class Scope(AsyncLocalTenantContext owner, TenantScope? previous) : IDisposable
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
