using System.Globalization;
using Kun.Core.Domain;
using Kun.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kun.EntityFrameworkCore.Interceptors;

/// <summary>在业务保存前填充审计字段，保留创建字段，拒绝无法转换的已认证主体。</summary>
public sealed class AuditFieldsSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entry in context.ChangeTracker.Entries<IAuditedObject>().ToArray())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(IAuditedObject.CreatedAt)).CurrentValue = now;
                SetActor(entry, "CreatedBy");
                entry.Property(nameof(IAuditedObject.UpdatedAt)).CurrentValue = null;
                if (entry.Metadata.FindProperty("UpdatedBy") is not null) entry.Property("UpdatedBy").CurrentValue = null;
            }
            else if (entry.State == EntityState.Modified)
            {
                Preserve(entry, nameof(IAuditedObject.CreatedAt));
                Preserve(entry, "CreatedBy");
                entry.Property(nameof(IAuditedObject.UpdatedAt)).CurrentValue = now;
                SetActor(entry, "UpdatedBy");
            }
        }
    }

    private static void Preserve(EntityEntry entry, string name)
    {
        if (entry.Metadata.FindProperty(name) is null) return;
        var property = entry.Property(name);
        property.CurrentValue = property.OriginalValue;
        property.IsModified = false;
    }

    private void SetActor(EntityEntry entry, string name)
    {
        var metadata = entry.Metadata.FindProperty(name);
        if (metadata is null) return;
        var subject = currentUser.IsAuthenticated ? currentUser.UserId : null;
        var type = Nullable.GetUnderlyingType(metadata.ClrType) ?? metadata.ClrType;
        object? value;
        if (subject is null)
        {
            if (metadata.ClrType.IsValueType && Nullable.GetUnderlyingType(metadata.ClrType) is null)
                throw new InvalidOperationException($"{name} 必须使用可空用户键，不能以默认值冒充系统主体。");
            value = null;
        }
        else if (type == typeof(string)) value = subject;
        else if (type == typeof(Guid) && Guid.TryParse(subject, out var guid)) value = guid;
        else if (type == typeof(long) && long.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) value = number;
        else if (type == typeof(int) && int.TryParse(subject, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) value = integer;
        else throw new InvalidOperationException($"已认证主体不能转换为 {name} 的用户键类型 {type.Name}。");
        entry.Property(name).CurrentValue = value;
    }
}
