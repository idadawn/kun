using Kun.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kun.Saga.Persistence.Ef;

/// <summary>执行器和超时检测器共用实例行锁；锁覆盖步骤、上下文、队列和状态提交。</summary>
internal static class SagaRowLock
{
    internal static async Task<SagaInstance?> TryAcquireAsync(KunDbContext context, Guid sagaId, CancellationToken ct)
    {
        if (context.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            throw new NotSupportedException("The default Saga executor requires PostgreSQL row locking.");
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Saga row locking requires an active transaction.");
        var entity = context.Model.FindEntityType(typeof(SagaInstance))!;
        var identifier = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var table = sql.DelimitIdentifier(identifier.Name, identifier.Schema);
        var column = sql.DelimitIdentifier(entity.FindProperty(nameof(SagaInstance.Id))!.GetColumnName(identifier)!);
        var statement = $"SELECT * FROM {table} WHERE {column} = {{0}} FOR UPDATE SKIP LOCKED";
        var rows = await context.Set<SagaInstance>().FromSqlRaw(statement, sagaId)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.SingleOrDefault();
    }
}
