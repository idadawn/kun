using Kun.EventBus.Background;
using Kun.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kun.EntityFrameworkCore;

/// <summary>默认 PostgreSQL Outbox 存储：用 SKIP LOCKED 在同一事务中领取、发布并更新批次。</summary>
public sealed class EfOutboxMessageStore(KunDbContext context, TimeProvider clock) : IOutboxBatchStore, IAsyncDisposable
{
    private IDbContextTransaction? _transaction;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        if (_transaction is not null || context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Outbox 发布批次需要独立事务上下文。");
        if (context.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            throw new NotSupportedException("默认 Outbox 行锁实现要求 PostgreSQL。");
        _transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var entity = context.Model.FindEntityType(typeof(OutboxMessage))!;
        var identifier = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string Column(string name) => sql.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(identifier)!);
        var table = sql.DelimitIdentifier(identifier.Name, identifier.Schema);
        var statement = $"SELECT * FROM {table} WHERE {Column(nameof(OutboxMessage.Status))} = 0 " +
            $"AND ({Column(nameof(OutboxMessage.NextAttemptAt))} IS NULL OR {Column(nameof(OutboxMessage.NextAttemptAt))} <= {{1}}) " +
            $"ORDER BY {Column(nameof(OutboxMessage.OccurredAt))}, {Column(nameof(OutboxMessage.Id))} LIMIT {{0}} FOR UPDATE SKIP LOCKED";
        return await context.OutboxMessages.FromSqlRaw(statement, batchSize, clock.GetUtcNow())
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (_transaction is null || context.Entry(message).State == EntityState.Detached)
            throw new InvalidOperationException("只允许更新当前批次已领取的消息。");
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CompleteBatchAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) throw new InvalidOperationException("尚未领取批次。");
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await _transaction.DisposeAsync().ConfigureAwait(false);
    }
}
