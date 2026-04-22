using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Kun.Caching.Abstractions;
using Kun.Caching.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kun.Caching.Implementations;

/// <summary>
/// Distributed <see cref="IKunCache"/> backed by an
/// <see cref="IDistributedCache"/> instance (typically
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c>). Values are
/// serialized via the registered <see cref="IKunCacheSerializer"/>.
/// </summary>
/// <remarks>
/// When <c>KunCachingOptions.FallbackToMemoryOnRedisFailure = true</c>,
/// connection / socket failures are swallowed and the operation is
/// routed to an in-memory sidecar cache for the duration of the
/// outage. The fallback is per-process; other replicas will not see
/// writes made while Redis is unreachable.
/// </remarks>
public sealed partial class RedisKunCache : IKunCache
{
    private readonly IDistributedCache _distributed;
    private readonly IKunCacheSerializer _serializer;
    private readonly MemoryKunCache? _fallback;
    private readonly KunCachingOptions _options;
    private readonly ILogger<RedisKunCache> _logger;
    private readonly SingleFlightLockPool _locks = new();

    /// <summary>Creates a new <see cref="RedisKunCache"/>.</summary>
    public RedisKunCache(
        IDistributedCache distributed,
        IKunCacheSerializer serializer,
        IOptions<KunCachingOptions> options,
        ILogger<RedisKunCache> logger,
        MemoryKunCache? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(distributed);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _distributed = distributed;
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
        _fallback = _options.FallbackToMemoryOnRedisFailure ? fallback : null;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("IKunCache.GetAsync contract.")]
    [RequiresDynamicCode("IKunCache.GetAsync contract.")]
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var full = KeyPrefixer.Apply(_options, key);
        try
        {
            var data = await _distributed.GetAsync(full, cancellationToken).ConfigureAwait(false);
            return data is null ? default : _serializer.Deserialize<T>(data);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Log.RedisFailureReadThrough(_logger, full, ex);
            if (_fallback is null)
            {
                throw;
            }
            return await _fallback.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("IKunCache.SetAsync contract.")]
    [RequiresDynamicCode("IKunCache.SetAsync contract.")]
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var full = KeyPrefixer.Apply(_options, key);
        var payload = _serializer.Serialize(value);

        var entryOptions = new DistributedCacheEntryOptions();
        var effectiveTtl = ttl ?? _options.DefaultTtl;
        if (effectiveTtl is { } window)
        {
            entryOptions.AbsoluteExpirationRelativeToNow = window;
        }

        try
        {
            await _distributed.SetAsync(full, payload, entryOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Log.RedisFailureWriteThrough(_logger, full, ex);
            if (_fallback is null)
            {
                throw;
            }
            await _fallback.SetAsync(key, value, ttl, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var full = KeyPrefixer.Apply(_options, key);
        try
        {
            await _distributed.RemoveAsync(full, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            Log.RedisFailureRemove(_logger, full, ex);
            if (_fallback is null)
            {
                throw;
            }
            await _fallback.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
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

    private static bool IsTransient(Exception ex)
        => ex is RedisConnectionException
            or RedisTimeoutException
            or SocketException;

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
            Message = "Redis cache read failed for key {Key}; falling back to memory if enabled.")]
        public static partial void RedisFailureReadThrough(ILogger logger, string key, Exception ex);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
            Message = "Redis cache write failed for key {Key}; falling back to memory if enabled.")]
        public static partial void RedisFailureWriteThrough(ILogger logger, string key, Exception ex);

        [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
            Message = "Redis cache remove failed for key {Key}; falling back to memory if enabled.")]
        public static partial void RedisFailureRemove(ILogger logger, string key, Exception ex);
    }
}
