namespace Kun.EventBus.Background;

/// <summary>
/// Provides a distributed lease that prevents multiple pods from publishing
/// the same outbox batch simultaneously.
/// </summary>
public interface IOutboxLockProvider
{
    /// <summary>
    /// Tries to acquire a distributed lock with the given <paramref name="lockName"/>.
    /// </summary>
    /// <param name="lockName">A well-known name that all competing pods use (e.g. <c>"outbox-publisher"</c>).</param>
    /// <param name="ttl">How long the lock is held before it auto-expires.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IAsyncDisposable"/> that releases the lock on disposal, or
    /// <see langword="null"/> when the lock is already held by another instance.
    /// </returns>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(string lockName, TimeSpan ttl, CancellationToken ct = default);
}
