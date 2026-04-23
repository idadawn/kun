namespace Kun.Identity.Abstractions.Session;

/// <summary>
/// Backend for cookie-based session data. Implementations ship in
/// <c>Kun.Identity.Implementations.Session</c> (in-memory for tests /
/// single-process dev; Redis-backed via <c>Kun.Caching</c> for
/// production). Implementations must be safe for concurrent use from
/// multiple request threads.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Reads the session payload for <paramref name="sessionId"/>; returns
    /// <see langword="null"/> on miss or when the entry has expired.
    /// </summary>
    Task<SessionData?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="data"/> under <paramref name="sessionId"/>
    /// with an absolute TTL of <paramref name="ttl"/>. Overwrites any
    /// existing entry atomically.
    /// </summary>
    Task SetAsync(string sessionId, SessionData data, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Removes the session. No-op when the session id is unknown.</summary>
    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}
