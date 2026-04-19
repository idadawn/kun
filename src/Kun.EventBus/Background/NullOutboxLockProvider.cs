namespace Kun.EventBus.Background;

/// <summary>
/// No-op <see cref="IOutboxLockProvider"/> for single-instance deployments.
/// Always succeeds and returns a disposable that does nothing.
/// This is the default provider registered by <c>AddKunOutboxPublisher</c>.
/// </summary>
public sealed class NullOutboxLockProvider : IOutboxLockProvider
{
    /// <summary>A shared singleton instance of <see cref="NullOutboxLockProvider"/>.</summary>
    public static readonly NullOutboxLockProvider Instance = new();

    /// <inheritdoc/>
    public ValueTask<IAsyncDisposable?> TryAcquireAsync(string lockName, TimeSpan ttl, CancellationToken ct = default)
        => ValueTask.FromResult<IAsyncDisposable?>(NullLease.Instance);

    private sealed class NullLease : IAsyncDisposable
    {
        internal static readonly NullLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
