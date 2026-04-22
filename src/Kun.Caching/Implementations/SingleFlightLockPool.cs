using System.Collections.Concurrent;

namespace Kun.Caching.Implementations;

/// <summary>
/// Per-key <see cref="SemaphoreSlim"/> pool used by
/// <c>IKunCache.GetOrSetAsync</c> implementations to guarantee the
/// value factory runs exactly once under concurrent access on the
/// same process.
/// </summary>
/// <remarks>
/// Each key gets its own reference-counted semaphore. When the last
/// waiter releases, the semaphore is removed from the dictionary so
/// the memory footprint is bounded by the in-flight working set
/// rather than the lifetime key universe.
/// </remarks>
internal sealed class SingleFlightLockPool
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        Entry entry;
        while (true)
        {
            entry = _entries.GetOrAdd(key, _ => new Entry());
            if (entry.TryAcquire())
            {
                break;
            }
            // The entry was removed between GetOrAdd and TryAcquire — retry.
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        entry.Semaphore.Release();
        if (entry.ReleaseReference())
        {
            // Best-effort removal; if a concurrent AcquireAsync resurrected
            // the refcount the TryRemove predicate declines the removal.
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Entry
    {
        private int _refCount;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAcquire()
        {
            int current;
            do
            {
                current = Volatile.Read(ref _refCount);
                if (current < 0)
                {
                    return false; // Entry is being removed.
                }
            }
            while (Interlocked.CompareExchange(ref _refCount, current + 1, current) != current);
            return true;
        }

        public bool ReleaseReference()
        {
            var next = Interlocked.Decrement(ref _refCount);
            if (next == 0)
            {
                // Publish a sentinel so TryAcquire races reject and retry the outer loop.
                return Interlocked.CompareExchange(ref _refCount, -1, 0) == 0;
            }
            return false;
        }
    }

    private sealed class Releaser(SingleFlightLockPool pool, string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                pool.Release(key, entry);
            }
        }
    }
}
