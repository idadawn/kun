using Npgsql;
using NpgsqlTypes;

namespace Kun.Audit.EntityFrameworkCore;

/// <summary>按租户串行追加的 PostgreSQL 审计链；记录与链头在同一事务中提交。</summary>
public sealed class PostgresAuditChain
    : IAuditChainState, IAuditSink
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _schema;
    private readonly AsyncLocal<AppendScope?> _current = new();

    /// <summary>数据源由调用方管理；schema 必须是简单 PostgreSQL 标识符。</summary>
    public PostgresAuditChain(NpgsqlDataSource dataSource, string schema = "audit")
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        if (string.IsNullOrEmpty(schema) || schema.Length > 63 ||
            !(char.IsAsciiLetter(schema[0]) || schema[0] == '_') ||
            schema.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("schema 必须是 1–63 个 ASCII 字母、数字或下划线，不能以数字开头。", nameof(schema));
        _dataSource = dataSource;
        _schema = '"' + schema + '"';
    }

    /// <summary>显式初始化审计存储；生产中由迁移阶段调用，不在业务追加时建表。</summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand($"""
            CREATE SCHEMA IF NOT EXISTS {_schema};
            CREATE TABLE IF NOT EXISTS {_schema}.audit_chain_state (
                tenant_key text PRIMARY KEY, tip_hash text, tip_seq bigint NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS {_schema}.audit_entries (
                id uuid PRIMARY KEY, tenant_key text NOT NULL, seq bigint NOT NULL,
                tenant_id text, actor_id text, actor_type text NOT NULL,
                command_name text NOT NULL, occurred_at timestamptz NOT NULL,
                occurred_at_ticks bigint NOT NULL, latency_ms bigint NOT NULL,
                outcome int NOT NULL, error_code text, prev_hash text,
                content_hash text NOT NULL UNIQUE,
                UNIQUE (tenant_key, seq), UNIQUE NULLS NOT DISTINCT (tenant_key, prev_hash));
            """);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<string> AppendAsync(string? tenantKey, Func<string?, ValueTask<string>> computeNewHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computeNewHash);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await AppendAttemptAsync(tenantKey, computeNewHash, cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (attempt < 15 &&
                ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(100, 5 * (attempt + 1))), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<string> AppendAttemptAsync(string? tenantKey,
        Func<string?, ValueTask<string>> computeNewHash, CancellationToken cancellationToken)
    {
        if (_current.Value is not null) throw new InvalidOperationException("不允许嵌套追加审计链。");
        var key = Key(tenantKey);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // 每个租户的唯一链头行是串行化点。READ COMMITTED 让排队副本在取得行锁后
        // 读取最新 tip；SERIALIZABLE 的旧快照会令持续追加的副本反复 40001 而饥饿。
        // 行锁保持到记录、链头一并提交，数据库唯一约束再阻止前序分叉。
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        await using (var create = new NpgsqlCommand($"INSERT INTO {_schema}.audit_chain_state (tenant_key) VALUES (@key) ON CONFLICT DO NOTHING", connection, transaction))
        {
            create.Parameters.AddWithValue("key", key);
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        string? previous;
        long sequence;
        await using (var select = new NpgsqlCommand($"SELECT tip_hash, tip_seq FROM {_schema}.audit_chain_state WHERE tenant_key = @key FOR UPDATE", connection, transaction))
        {
            select.Parameters.AddWithValue("key", key);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            previous = reader.IsDBNull(0) ? null : reader.GetString(0);
            sequence = reader.GetInt64(1) + 1;
        }
        var scope = new AppendScope(connection, transaction, tenantKey, previous, sequence);
        _current.Value = scope;
        try
        {
            var next = await computeNewHash(previous).ConfigureAwait(false);
            if (scope.WrittenHash is null || next != scope.WrittenHash)
                throw new InvalidOperationException("链头只能推进到当前事务实际写入的一条审计记录。");
            await using var update = new NpgsqlCommand($"UPDATE {_schema}.audit_chain_state SET tip_hash = @hash, tip_seq = @seq WHERE tenant_key = @key", connection, transaction);
            update.Parameters.AddWithValue("hash", next);
            update.Parameters.AddWithValue("seq", sequence);
            update.Parameters.AddWithValue("key", key);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return next;
        }
        finally
        {
            _current.Value = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var scope = _current.Value ?? throw new InvalidOperationException("审计记录必须在同一 PostgresAuditChain.AppendAsync 内写入。");
        if (scope.WrittenHash is not null || entry.TenantId != scope.TenantId || entry.PrevHash != scope.Previous ||
            entry.ContentHash != AuditContentHasher.Compute(entry))
            throw new InvalidOperationException("审计记录的租户、前序或内容哈希与当前追加作用域不一致。");
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {_schema}.audit_entries
                (id, tenant_key, seq, tenant_id, actor_id, actor_type, command_name, occurred_at,
                 occurred_at_ticks, latency_ms, outcome, error_code, prev_hash, content_hash)
            VALUES (@id, @key, @seq, @tenant, @actor, @actor_type, @command, @time,
                    @ticks, @latency, @outcome, @error, @prev, @hash)
            """, scope.Connection, scope.Transaction);
        command.Parameters.AddWithValue("id", entry.Id);
        command.Parameters.AddWithValue("key", Key(entry.TenantId));
        command.Parameters.AddWithValue("seq", scope.Sequence);
        AddText(command, "tenant", entry.TenantId);
        AddText(command, "actor", entry.ActorId);
        AddText(command, "actor_type", entry.ActorType);
        AddText(command, "command", entry.CommandName);
        command.Parameters.AddWithValue("time", entry.OccurredAt.UtcDateTime);
        // PostgreSQL 时间戳只有微秒精度；额外保存 ticks，保证重放历史哈希不丢精度。
        command.Parameters.AddWithValue("ticks", entry.OccurredAt.UtcTicks);
        command.Parameters.AddWithValue("latency", entry.LatencyMs);
        command.Parameters.AddWithValue("outcome", (int)entry.Outcome);
        AddText(command, "error", entry.ErrorCode);
        AddText(command, "prev", entry.PrevHash);
        AddText(command, "hash", entry.ContentHash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        scope.WrittenHash = entry.ContentHash;
    }

    /// <summary>按持久化序号读取指定租户的链；调用方负责授权。</summary>
    public async IAsyncEnumerable<AuditEntry> ReadAsync(string? tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand($"""
            SELECT id, tenant_id, actor_id, command_name, occurred_at_ticks,
                   latency_ms, outcome, error_code, prev_hash, content_hash, actor_type
            FROM {_schema}.audit_entries WHERE tenant_key = @key ORDER BY seq
            """);
        command.Parameters.AddWithValue("key", Key(tenantId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new AuditEntry(reader.GetGuid(0), Text(reader, 1), Text(reader, 2), reader.GetString(3),
                new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero), reader.GetInt64(5),
                (AuditOutcome)reader.GetInt32(6), Text(reader, 7), Text(reader, 8), reader.GetString(9))
            { ActorType = reader.GetString(10) };
        }
    }

    private static string Key(string? tenant) => tenant is null ? "global:" : "tenant:" + tenant;
    private static string? Text(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static void AddText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, (object?)value ?? DBNull.Value);

    private sealed class AppendScope(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string? tenantId, string? previous, long sequence)
    {
        public NpgsqlConnection Connection { get; } = connection;
        public NpgsqlTransaction Transaction { get; } = transaction;
        public string? TenantId { get; } = tenantId;
        public string? Previous { get; } = previous;
        public long Sequence { get; } = sequence;
        public string? WrittenHash { get; set; }
    }
}
