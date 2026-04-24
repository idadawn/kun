using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Kun.Audit;

/// <summary>
/// Canonicalises audit-entry fields and chains them with SHA-256:
/// <c>hash = sha256(prevHash || canonical(entryFields))</c>.
/// The output is lower-case hex so it is safe to embed in DB columns, URLs,
/// and Markdown tables without re-encoding.
/// </summary>
public static class AuditContentHasher
{
    /// <summary>Computes the chain hash for one audit entry.</summary>
    /// <param name="id">Entry id.</param>
    /// <param name="tenantId">Tenant id, or <see langword="null"/>.</param>
    /// <param name="actorId">Actor id, or <see langword="null"/>.</param>
    /// <param name="commandName">Command type short name. Must be non-empty.</param>
    /// <param name="occurredAt">UTC timestamp (formatted with <c>"O"</c> round-trip format for determinism).</param>
    /// <param name="latencyMs">Wall-clock latency in milliseconds.</param>
    /// <param name="outcome">Success / Exception.</param>
    /// <param name="errorCode">Exception type name on failure, or <see langword="null"/>.</param>
    /// <param name="prevHash">Previous entry's <see cref="AuditEntry.ContentHash"/>, or <see langword="null"/> for the first entry.</param>
    /// <returns>Lower-case hex SHA-256.</returns>
    public static string Compute(
        Guid id,
        string? tenantId,
        string? actorId,
        string commandName,
        DateTimeOffset occurredAt,
        long latencyMs,
        AuditOutcome outcome,
        string? errorCode,
        string? prevHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(commandName);

        // Canonical form separates fields with a pipe and pins the timestamp
        // format so the same logical entry always produces the same hash
        // regardless of the caller's culture / timezone serialisation.
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{id:N}|{tenantId}|{actorId}|{commandName}|{occurredAt.ToUniversalTime():O}|{latencyMs}|{outcome}|{errorCode}");

        var prefix = prevHash ?? string.Empty;

        var bytes = Encoding.UTF8.GetBytes(prefix + canonical);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
