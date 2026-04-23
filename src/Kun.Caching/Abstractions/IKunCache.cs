using System.Diagnostics.CodeAnalysis;

namespace Kun.Caching.Abstractions;

/// <summary>
/// Unified cache abstraction for Kun Framework consumers. Implementations
/// back to an in-process <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
/// or to a distributed <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// (Redis via StackExchange) — see
/// <c>Kun.Caching.Implementations.MemoryKunCache</c> and
/// <c>Kun.Caching.Implementations.RedisKunCache</c>.
/// </summary>
/// <remarks>
/// The generic methods are annotated with
/// <see cref="RequiresUnreferencedCodeAttribute"/> and
/// <see cref="RequiresDynamicCodeAttribute"/> because the default
/// <c>System.Text.Json</c>-backed serializer walks the type argument
/// by reflection. AOT-strict consumers can replace the default
/// serializer with a source-generated implementation; see
/// <see cref="IKunCacheSerializer"/>.
/// </remarks>
public interface IKunCache
{
    /// <summary>Reads the cached value for <paramref name="key"/>; returns <see langword="null"/> on miss.</summary>
    [RequiresUnreferencedCode("Default serializer uses reflection. Pass a source-generated IKunCacheSerializer for AOT safety.")]
    [RequiresDynamicCode("Default serializer uses reflection. Pass a source-generated IKunCacheSerializer for AOT safety.")]
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="value"/> under <paramref name="key"/> with an
    /// optional TTL. When <paramref name="ttl"/> is <see langword="null"/>,
    /// <c>KunCachingOptions.DefaultTtl</c> applies; when that is also
    /// <see langword="null"/>, the entry never expires.
    /// </summary>
    [RequiresUnreferencedCode("Default serializer uses reflection. Pass a source-generated IKunCacheSerializer for AOT safety.")]
    [RequiresDynamicCode("Default serializer uses reflection. Pass a source-generated IKunCacheSerializer for AOT safety.")]
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>Removes the entry for <paramref name="key"/>. No-op when the key is absent.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the cached byte payload for <paramref name="key"/>; returns
    /// <see langword="null"/> on miss. Reflection-free path intended for
    /// AOT-strict callers (e.g. <c>Kun.Identity</c>'s Redis session
    /// store) that already own their serialization — typically via a
    /// source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
    /// </summary>
    Task<byte[]?> GetBytesAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="value"/> byte-for-byte under
    /// <paramref name="key"/>. When <paramref name="ttl"/> is
    /// <see langword="null"/>, <c>KunCachingOptions.DefaultTtl</c> applies;
    /// when both are <see langword="null"/>, the entry never expires.
    /// Reflection-free path; see <see cref="GetBytesAsync"/>.
    /// </summary>
    Task SetBytesAsync(string key, byte[] value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached value, invoking <paramref name="factory"/> exactly
    /// once under concurrent access on the same process when the key is
    /// absent. The produced value is written to the cache under
    /// <paramref name="key"/> before being returned.
    /// </summary>
    /// <remarks>
    /// Single-flight protection is local to the process — cross-process
    /// stampedes can still occur. Use an external distributed lock for
    /// that scenario.
    /// </remarks>
    [RequiresUnreferencedCode("Default serializer uses reflection. Pass a source-generated IKunCacheSerializer for AOT safety.")]
    [RequiresDynamicCode("Default serializer uses reflection. Pass a source-generated IKunCacheSerializer for AOT safety.")]
    Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);
}
