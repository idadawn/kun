using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kun.Caching.Abstractions;
using Kun.Identity.Abstractions;
using Kun.Identity.Abstractions.Session;
using StackExchange.Redis;

namespace Kun.Identity.Implementations.Session;

/// <summary>Redis session registry with atomic user/provider indexes, renewal and logout replay protection.</summary>
public sealed class RedisSessionStore : ISessionRegistry
{
    private readonly IKunCache? _legacy;
    private readonly IDatabase? _database;
    private readonly TimeProvider _clock;
    private readonly string _prefix = "kun:identity:{sessions}:";

    /// <summary>Compatibility-only byte-cache storage; indexed revocation requires the connection-based constructor.</summary>
    [Obsolete("Use the IConnectionMultiplexer constructor for atomic renewal and revocation.", DiagnosticId = "KUNSESSION001")]
    public RedisSessionStore(IKunCache cache) { ArgumentNullException.ThrowIfNull(cache); _legacy = cache; _clock = TimeProvider.System; }

    /// <summary>Creates an atomic registry. All registry keys share a hash slot; prefix isolates applications.</summary>
    public RedisSessionStore(IConnectionMultiplexer connection, TimeProvider clock, string keyPrefix = "kun:identity:")
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(clock); ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        if (keyPrefix.Contains('{', StringComparison.Ordinal) || keyPrefix.Contains('}', StringComparison.Ordinal)) throw new ArgumentException("Session prefix cannot contain hash-tag braces.", nameof(keyPrefix));
        _database = connection.GetDatabase(); _clock = clock; _prefix = keyPrefix + "{sessions}:";
    }

    /// <inheritdoc />
    public async Task<SessionData?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        if (_legacy is not null)
        {
            var bytes = await _legacy.GetBytesAsync("kun:session:" + sessionId, cancellationToken).ConfigureAwait(false);
            return bytes is null || bytes.Length == 0 ? null : JsonSerializer.Deserialize(bytes, SessionJsonContext.Default.SessionData);
        }
        var payload = await Database.HashGetAsync(SessionKey(sessionId), "payload").WaitAsync(cancellationToken).ConfigureAwait(false);
        return payload.IsNullOrEmpty ? null : JsonSerializer.Deserialize(payload.ToString(), SessionJsonContext.Default.SessionData);
    }

    /// <inheritdoc />
    public async Task SetAsync(string sessionId, SessionData data, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId); ArgumentNullException.ThrowIfNull(data); ArgumentException.ThrowIfNullOrWhiteSpace(data.UserId);
        ValidateTtl(ttl);
        var payload = JsonSerializer.SerializeToUtf8Bytes(data, SessionJsonContext.Default.SessionData);
        if (_legacy is not null) { await _legacy.SetBytesAsync("kun:session:" + sessionId, payload, ttl, cancellationToken).ConfigureAwait(false); return; }
        var subject = data.Issuer is null ? "" : SubjectKey(data.Issuer, data.UserId);
        var sid = data.Issuer is null || data.ProviderSessionId is null ? "" : ProviderSessionKey(data.Issuer, data.ProviderSessionId);
        await Database.ScriptEvaluateAsync(RemoveFunction + IndexFunction + """
            remove(KEYS[1])
            redis.call('HSET', KEYS[1], 'payload', ARGV[1], 'user', KEYS[2], 'subject', ARGV[3], 'sid', ARGV[4])
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            index(KEYS[2], KEYS[1], tonumber(ARGV[2]))
            if ARGV[3] ~= '' then index(ARGV[3], KEYS[1], tonumber(ARGV[2])) end
            if ARGV[4] ~= '' then index(ARGV[4], KEYS[1], tonumber(ARGV[2])) end
            return 1
            """, [SessionKey(sessionId), UserKey(data.UserId)], [payload, Milliseconds(ttl), subject, sid]).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        if (_legacy is not null) { await _legacy.RemoveAsync("kun:session:" + sessionId, cancellationToken).ConfigureAwait(false); return; }
        await Database.ScriptEvaluateAsync(RemoveFunction + "return remove(KEYS[1])", [SessionKey(sessionId)]).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RenewAsync(string sessionId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId); ValidateTtl(ttl);
        var result = await Database.ScriptEvaluateAsync(IndexFunction + """
            if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
            local links = redis.call('HMGET', KEYS[1], 'user', 'subject', 'sid')
            for _, key in ipairs(links) do if key and key ~= '' then index(key, KEYS[1], tonumber(ARGV[1])) end end
            return 1
            """, [SessionKey(sessionId)], [Milliseconds(ttl)]).WaitAsync(cancellationToken).ConfigureAwait(false);
        return (long)result == 1;
    }

    /// <inheritdoc />
    public async Task<int> RevokeUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var result = await Database.ScriptEvaluateAsync(RemoveFunction + """
            local ids = redis.call('ZRANGE', KEYS[1], 0, -1)
            local count = 0
            for _, id in ipairs(ids) do count = count + remove(id) end
            redis.call('DEL', KEYS[1])
            return count
            """, [UserKey(userId)]).WaitAsync(cancellationToken).ConfigureAwait(false);
        return checked((int)(long)result);
    }

    /// <inheritdoc />
    public async Task<int> ApplyLogoutAsync(OidcLogoutToken logout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logout); ArgumentException.ThrowIfNullOrWhiteSpace(logout.Issuer); ArgumentException.ThrowIfNullOrWhiteSpace(logout.TokenId);
        if (string.IsNullOrWhiteSpace(logout.Subject) && string.IsNullOrWhiteSpace(logout.SessionId)) throw new ArgumentException("Logout requires a subject or provider session.", nameof(logout));
        var remaining = logout.ExpiresAt.AddSeconds(30) - _clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) return 0;
        var subject = logout.Subject is null ? "" : SubjectKey(logout.Issuer, logout.Subject);
        var index = logout.SessionId is null ? subject : ProviderSessionKey(logout.Issuer, logout.SessionId);
        var result = await Database.ScriptEvaluateAsync(RemoveFunction + """
            if not redis.call('SET', KEYS[2], '1', 'PX', ARGV[1], 'NX') then return 0 end
            local ids = redis.call('ZRANGE', KEYS[1], 0, -1)
            local count = 0
            for _, id in ipairs(ids) do
                if ARGV[2] == '' or redis.call('HGET', id, 'subject') == ARGV[2] then count = count + remove(id) end
            end
            return count
            """, [index, NonceKey("logout:" + logout.Issuer, logout.TokenId)], [Milliseconds(remaining), subject]).WaitAsync(cancellationToken).ConfigureAwait(false);
        return checked((int)(long)result);
    }

    /// <inheritdoc />
    public Task<bool> TryUseNonceAsync(string purpose, string nonce, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose); ArgumentException.ThrowIfNullOrWhiteSpace(nonce); ValidateTtl(ttl);
        return Database.StringSetAsync(NonceKey(purpose, nonce), "1", ttl, when: When.NotExists).WaitAsync(cancellationToken);
    }

    private IDatabase Database => _database ?? throw new NotSupportedException("Byte-cache sessions do not support atomic revocation. Configure the Redis connection-based registry.");
    private string SessionKey(string id) => _prefix + "s:" + Hash(id);
    private string UserKey(string id) => _prefix + "u:" + Hash(id);
    private string SubjectKey(string issuer, string subject) => _prefix + "o:" + PairHash(issuer, subject);
    private string ProviderSessionKey(string issuer, string sid) => _prefix + "p:" + PairHash(issuer, sid);
    private string NonceKey(string purpose, string nonce) => _prefix + "n:" + PairHash(purpose, nonce);
    private static string PairHash(string first, string second) => Hash(first.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + first + second);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static long Milliseconds(TimeSpan ttl) => checked((long)Math.Ceiling(ttl.TotalMilliseconds));
    private static void ValidateTtl(TimeSpan ttl) => ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

    private const string RemoveFunction = """
        local function remove(id)
            local links = redis.call('HMGET', id, 'user', 'subject', 'sid')
            for _, key in ipairs(links) do if key and key ~= '' then redis.call('ZREM', key, id) end end
            return redis.call('DEL', id)
        end

        """;
    private const string IndexFunction = """
        local function index(key, id, ttl)
            local time = redis.call('TIME')
            local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
            redis.call('ZREMRANGEBYSCORE', key, '-inf', now)
            redis.call('ZADD', key, now + ttl, id)
            if redis.call('PTTL', key) < ttl then redis.call('PEXPIRE', key, ttl) end
        end

        """;
}
