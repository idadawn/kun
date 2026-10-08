using System.Runtime.CompilerServices;
using Kun.Audit.Abstractions;
using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Kun.Audit.EntityFrameworkCore;

/// <summary>在数据库内完成租户过滤与限量，禁止以查询参数切换当前租户。</summary>
public sealed class EfAuditReader<TContext>(TContext context, ITenantContext tenantContext) : IAuditReader
    where TContext : DbContext
{
    /// <inheritdoc />
    public async IAsyncEnumerable<AuditLog> QueryAsync(AuditQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.TenantId) || query.TenantId != tenantContext.TenantId)
            throw new InvalidOperationException("审计查询必须匹配当前租户上下文。");
        if (query.Limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(query), "Limit 必须为 1–1000。");
        var rows = context.Set<AuditLog>().AsNoTracking().Where(log => log.TenantId == query.TenantId);
        if (query.ActorId is not null) rows = rows.Where(log => log.ActorId == query.ActorId);
        if (query.Action is not null) rows = rows.Where(log => log.Action == query.Action);
        if (query.ResourceType is not null) rows = rows.Where(log => log.ResourceType == query.ResourceType);
        if (query.ResourceId is not null) rows = rows.Where(log => log.ResourceId == query.ResourceId);
        if (query.From is not null) rows = rows.Where(log => log.Timestamp >= query.From);
        if (query.To is not null) rows = rows.Where(log => log.Timestamp < query.To);
        await foreach (var row in rows.OrderByDescending(log => log.Timestamp).ThenByDescending(log => log.Id)
            .Take(query.Limit).AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return row;
    }
}
