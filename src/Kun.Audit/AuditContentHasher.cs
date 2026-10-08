using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kun.Audit;

/// <summary>
/// Canonicalises audit-entry fields and chains them with SHA-256:
/// <c>hash = sha256(prevHash || canonical(entryFields))</c>.
/// The output is lower-case hex so it is safe to embed in DB columns, URLs,
/// and Markdown tables without re-encoding.
/// </summary>
public static class AuditContentHasher
{
    /// <summary>计算包含主体类型的哈希；历史 Unknown 条目兼容既有算法。</summary>
    public static string Compute(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var legacy = Compute(entry.Id, entry.TenantId, entry.ActorId, entry.CommandName,
            entry.OccurredAt, entry.LatencyMs, entry.Outcome, entry.ErrorCode, entry.PrevHash);
        if (entry.ActorType == AuditActorType.Unknown) return legacy;
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ActorType);
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("id", entry.Id);
            json.WriteString("tenantId", entry.TenantId);
            json.WriteString("actorId", entry.ActorId);
            json.WriteString("actorType", entry.ActorType);
            json.WriteString("commandName", entry.CommandName);
            json.WriteString("occurredAt", entry.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            json.WriteNumber("latencyMs", entry.LatencyMs);
            json.WriteNumber("outcome", (int)entry.Outcome);
            json.WriteString("errorCode", entry.ErrorCode);
            json.WriteEndObject();
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(entry.PrevHash ?? string.Empty));
        hash.AppendData(buffer.WrittenSpan);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

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
