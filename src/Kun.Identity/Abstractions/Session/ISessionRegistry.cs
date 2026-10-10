namespace Kun.Identity.Abstractions.Session;

/// <summary>Atomic session lifecycle operations. Renewing a revoked session must never recreate it.</summary>
public interface ISessionRegistry : ISessionStore
{
    /// <summary>Extends an existing live session, returning false on expiration or revocation.</summary>
    Task<bool> RenewAsync(string sessionId, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Revokes every current application session for this user and returns the number removed.</summary>
    Task<int> RevokeUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Atomically consumes a validated logout token once and revokes matching issuer/subject/session entries.</summary>
    Task<int> ApplyLogoutAsync(OidcLogoutToken logout, CancellationToken cancellationToken = default);

    /// <summary>Consumes a nonce once within the supplied TTL. Used for browser transaction replay protection.</summary>
    Task<bool> TryUseNonceAsync(string purpose, string nonce, TimeSpan ttl, CancellationToken cancellationToken = default);
}
