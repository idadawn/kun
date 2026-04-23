using System.Text.Json;
using Kun.Caching.Abstractions;
using Kun.Identity.Abstractions.Session;

namespace Kun.Identity.Implementations.Session;

/// <summary>
/// Distributed <see cref="ISessionStore"/> backed by
/// <see cref="IKunCache"/>. Uses the reflection-free
/// <see cref="IKunCache.GetBytesAsync"/> / <see cref="IKunCache.SetBytesAsync"/>
/// surface so serialization runs through the source-generated
/// <see cref="SessionJsonContext"/> and the AOT analyzer stays clean.
/// </summary>
/// <remarks>
/// Keys are prefixed with <c>kun:session:</c> in addition to any
/// <c>KunCachingOptions.KeyPrefix</c> the host has configured for the
/// shared cache. The session TTL supplied to <see cref="SetAsync"/> is
/// passed through verbatim — <see cref="IKunCache"/>'s own
/// <c>DefaultTtl</c> is not applied to session entries because sessions
/// that never expire would be a subtle security regression.
/// </remarks>
public sealed class RedisSessionStore : ISessionStore
{
    private const string KeyPrefix = "kun:session:";
    private readonly IKunCache _cache;

    /// <summary>Creates a new <see cref="RedisSessionStore"/>.</summary>
    public RedisSessionStore(IKunCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<SessionData?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var bytes = await _cache.GetBytesAsync(BuildKey(sessionId), cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        return JsonSerializer.Deserialize(bytes, SessionJsonContext.Default.SessionData);
    }

    /// <inheritdoc />
    public async Task SetAsync(string sessionId, SessionData data, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(data);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "Session TTL must be positive.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(data, SessionJsonContext.Default.SessionData);
        await _cache.SetBytesAsync(BuildKey(sessionId), payload, ttl, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        return _cache.RemoveAsync(BuildKey(sessionId), cancellationToken);
    }

    private static string BuildKey(string sessionId) => string.Concat(KeyPrefix, sessionId);
}
