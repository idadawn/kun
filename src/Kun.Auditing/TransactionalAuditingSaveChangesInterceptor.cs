using System.Diagnostics;
using System.Text.Json;
using Kun.Audit;
using Kun.Auditing.DependencyInjection;
using Kun.Auditing.Events;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.EventBus.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kun.Auditing;

/// <summary>在业务上下文中同时暂存 AuditLog 与 Outbox，任何一方失败均使整个保存失败。</summary>
public sealed class TransactionalAuditingSaveChangesInterceptor(IAuditableEntityRegistry registry,
    ITenantContext tenant, ICurrentUser user, AuditContext auditContext, TimeProvider clock) : SaveChangesInterceptor
{
    private readonly Dictionary<DbContext, List<object>> _staged = new();

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stage(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stage(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stage(DbContext? context)
    {
        if (context is null) return;
        RemoveStaged(context);
        var changes = context.ChangeTracker.Entries().Where(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
            registry.RegisteredTypes.Contains(e.Metadata.ClrType)).ToArray();
        var pending = new List<object>();
        _staged[context] = pending;
        foreach (var entry in changes)
        {
            var now = clock.GetUtcNow();
            var id = Guid.CreateVersion7();
            var actor = auditContext.ActorId ?? (user.IsAuthenticated ? user.UserId : null);
            var actorType = auditContext.ActorType == AuditActorType.System && user.IsAuthenticated
                ? AuditActorType.User : auditContext.ActorType;
            var trace = auditContext.TraceId ?? Activity.Current?.TraceId.ToString();
            var resourceId = PrimaryKey(entry);
            var @event = new AuditEventIntegrationEventV2(id, tenant.TenantId,
                user.IsAuthenticated ? user.UserId : null, actorType, actor, entry.State.ToString(),
                entry.Metadata.ClrType.Name, resourceId, trace, auditContext.IpAddress, now);
            var payload = JsonSerializer.Serialize(@event, AuditJsonContext.Default.AuditEventIntegrationEventV2);
            var log = new AuditLog
            {
                Id = id,
                TenantId = tenant.TenantId,
                ActorId = actor,
                ActorType = actorType,
                Action = @event.Action,
                ResourceType = @event.ResourceType,
                ResourceId = resourceId,
                TraceId = trace,
                Timestamp = now,
                IpAddress = auditContext.IpAddress
            };
            var message = new OutboxMessage(id, AuditingServiceCollectionExtensions.AuditEventV2StableId,
                payload, tenant.TenantId, Activity.Current?.Id, Activity.Current?.TraceStateString, now);
            context.Set<AuditLog>().Add(log);
            context.Set<OutboxMessage>().Add(message);
            pending.Add(log);
            pending.Add(message);
        }
    }

    private static string? PrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null ? null : string.Join('|', key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }

    private void RemoveStaged(DbContext? context)
    {
        if (context is null || !_staged.Remove(context, out var staged)) return;
        foreach (var row in staged)
            if (context.Entry(row).State == EntityState.Added) context.Entry(row).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null) _staged.Remove(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(SavedChanges(eventData, result));

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => RemoveStaged(eventData.Context);
    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RemoveStaged(eventData.Context);
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public override void SaveChangesCanceled(DbContextEventData eventData) => RemoveStaged(eventData.Context);
    /// <inheritdoc />
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RemoveStaged(eventData.Context);
        return Task.CompletedTask;
    }
}
