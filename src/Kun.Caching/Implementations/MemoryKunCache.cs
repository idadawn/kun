using System.Diagnostics.CodeAnalysis;
using Kun.Caching.Abstractions;
using Kun.Caching.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Kun.Caching.Implementations;

/// <summary>
/// In-process <see cref="IKunCache"/> backed by
/// <see cref="IMemoryCache"/>. Stores values directly — no
/// serialization round-trip — so mutations on cached reference
/// types are visible to other consumers. Callers that care about
/// isolation should cache immutable records or value types.
/// </summary>
public sealed class MemoryKunCache : IKunCache
{
    private readonly IMemoryCache _cache;
    private readonly KunCachingOptions _options;
    private readonly SingleFlightLockPool _locks = new();

    /// <summary>Creates a new <see cref="MemoryKunCache"/>.</summary>
    public MemoryKunCache(IMemoryCache cache, IOptions<KunCachingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("IKunCache.GetAsync contract.")]
    [RequiresDynamicCode("IKunCache.GetAsync contract.")]
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var full = KeyPrefixer.Apply(_options, key);
        if (_cache.TryGetValue(full, out var value) && value is T typed)
        {
            return Task.FromResult<T?>(typed);
        }
        return Task.FromResult<T?>(default);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("IKunCache.SetAsync contract.")]
    [RequiresDynamicCode("IKunCache.SetAsync contract.")]
    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var full = KeyPrefixer.Apply(_options, key);
        var entryOptions = new MemoryCacheEntryOptions();
        var effectiveTtl = ttl ?? _options.DefaultTtl;
        if (effectiveTtl is { } window)
        {
            entryOptions.AbsoluteExpirationRelativeToNow = window;
        }
        _cache.Set(full, value, entryOptions);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var full = KeyPrefixer.Apply(_options, key);
        _cache.Remove(full);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("IKunCache.GetOrSetAsync contract.")]
    [RequiresDynamicCode("IKunCache.GetOrSetAsync contract.")]
    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var hit = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (hit is not null)
        {
            return hit;
        }

        using var lease = await _locks.AcquireAsync(key, cancellationToken).ConfigureAwait(false);

        hit = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (hit is not null)
        {
            return hit;
        }

        var produced = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, produced, ttl, cancellationToken).ConfigureAwait(false);
        return produced;
    }
}
