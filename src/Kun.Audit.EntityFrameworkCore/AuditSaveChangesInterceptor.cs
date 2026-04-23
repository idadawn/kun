using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kun.Audit.Abstractions;
using Kun.Audit.DependencyInjection;
using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Kun.Core.Security;
using Kun.Core.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kun.Audit.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that emits one
/// <see cref="AuditLog"/> per <see cref="IAggregateRoot"/> (or
/// <see cref="IMultiTenant"/>) that was added / modified / deleted in
/// the current <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Change metadata is captured in <see cref="SavingChangesAsync"/>
/// (before EF finalises keys, so we have the original / current values)
/// and replayed into <see cref="IAuditWriter"/> after
/// <c>SaveChangesAsync</c> succeeds. This ordering avoids two hazards:
/// </para>
/// <list type="bullet">
///   <item>An audit-writer exception no longer rolls back the business transaction (unless the consumer sets <see cref="KunAuditOptions.SwallowWriterExceptions"/> to <see langword="false"/>).</item>
///   <item>Server-generated primary keys (PostgreSQL <c>IDENTITY</c>, DB-side GUIDs) are populated on the entity by the time we read them, so <see cref="AuditLog.ResourceId"/> is never blank.</item>
/// </list>
/// <para>
/// No reflection is used: the interceptor only inspects
/// <see cref="ChangeTracker"/> entries and Kun.Core marker interfaces.
/// Payload JSON is built with <see cref="Utf8JsonWriter"/> so the
/// interceptor stays portable to a future AOT-candidate module.
/// </para>
/// </remarks>
public sealed partial class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly AsyncLocal<List<PendingAudit>?> _pendingAudits = new();

    private readonly IAuditWriter _writer;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ISystemClock _clock;
    private readonly KunAuditOptions _options;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;

    /// <summary>Creates a new <see cref="AuditSaveChangesInterceptor"/>.</summary>
    public AuditSaveChangesInterceptor(
        IAuditWriter writer,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ISystemClock clock,
        IOptions<KunAuditOptions> options,
        ILogger<AuditSaveChangesInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _writer = writer;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var context = eventData.Context;
        if (context is not null)
        {
            _pendingAudits.Value = CaptureAudits(context);
        }
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var pending = _pendingAudits.Value;
        _pendingAudits.Value = null;
        if (pending is null || pending.Count == 0)
        {
            return result;
        }

        foreach (var item in pending)
        {
            var entry = ToAuditLog(item);
            try
            {
                await _writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (_options.SwallowWriterExceptions)
            {
                Log.WriterFailed(_logger, entry.Action, entry.ResourceType, entry.ResourceId, ex);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        _pendingAudits.Value = null;
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        _pendingAudits.Value = null;
        return Task.CompletedTask;
    }

    private static List<PendingAudit> CaptureAudits(DbContext context)
    {
        var captured = new List<PendingAudit>();
        // Materialise the entries snapshot before walking — the change tracker
        // mutates during iteration when shadow keys are assigned.
        var entries = context.ChangeTracker.Entries().ToList();
        foreach (var entry in entries)
        {
            // Skip non-audited infrastructure rows: OutboxMessage, AuditLog itself, etc.
            // The interceptor never audits audit rows — that would recurse forever.
            if (entry.Entity is AuditLog)
            {
                continue;
            }

            // Only audit aggregate roots or explicit multi-tenant rows to avoid
            // noisy change-log entries for owned-entity / value-object fragments.
            if (entry.Entity is not IAggregateRoot && entry.Entity is not IMultiTenant)
            {
                continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => "created",
                EntityState.Modified => "updated",
                EntityState.Deleted => "deleted",
                _ => null,
            };
            if (action is null)
            {
                continue;
            }

            // Soft-delete masquerades as Modified with IsDeleted flipped to true —
            // translate that back into a logical "deleted" audit so consumers see
            // the business-level transition, not the row-level UPDATE.
            if (action == "updated"
                && entry.Entity is ISoftDelete
                && entry.CurrentValues[nameof(ISoftDelete.IsDeleted)] is true
                && entry.OriginalValues[nameof(ISoftDelete.IsDeleted)] is false)
            {
                action = "deleted";
            }

            captured.Add(new PendingAudit(entry, action));
        }

        return captured;
    }

    private AuditLog ToAuditLog(PendingAudit pending)
    {
        var entry = pending.Entry;
        var clrType = entry.Entity.GetType();
        var resourceType = clrType.Name;
        var resourceId = ExtractResourceId(entry);
        var tenantId = entry.Entity is IMultiTenant multiTenant
            ? multiTenant.TenantId
            : _tenantContext.TenantId;
        var payloadJson = _options.CaptureEntityPayload ? BuildPayloadJson(entry) : null;
        var (actorId, actorType) = ResolveActor();
        var traceId = (_currentUser as ITracedCurrentUser)?.TraceId
            ?? Activity.Current?.TraceId.ToString();

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorId = actorId,
            ActorType = actorType,
            Action = $"{ToCanonical(resourceType)}.{pending.Action}",
            ResourceType = resourceType,
            ResourceId = resourceId,
            TraceId = traceId,
            Timestamp = _clock.UtcNow,
            PayloadJson = payloadJson,
            IpAddress = null,
        };
    }

    private (string? ActorId, string ActorType) ResolveActor()
    {
        if (_currentUser.IsAuthenticated && _currentUser.UserId is { Length: > 0 } userId)
        {
            return (userId, _options.DefaultActorType);
        }
        return (null, _options.AnonymousActorType);
    }

    private static string? ExtractResourceId(EntityEntry entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey();
        if (primaryKey is null)
        {
            return null;
        }

        var parts = new List<string>(primaryKey.Properties.Count);
        foreach (var prop in primaryKey.Properties)
        {
            var value = entry.Property(prop.Name).CurrentValue;
            if (value is null)
            {
                continue;
            }
            parts.Add(value.ToString() ?? string.Empty);
        }

        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => string.Join(':', parts),
        };
    }

    private static string? BuildPayloadJson(EntityEntry entry)
    {
        // Only capture the changed/current property values — raw Entity
        // serialization would demand a JsonTypeInfo per aggregate and
        // couples the interceptor to consumer types.
        using var buffer = new MemoryStream(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in entry.Properties)
            {
                var name = property.Metadata.Name;
                if (entry.State == EntityState.Modified && !property.IsModified)
                {
                    continue;
                }
                WriteJsonValue(writer, name, property.CurrentValue);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(name);
                break;
            case string s:
                writer.WriteString(name, s);
                break;
            case bool b:
                writer.WriteBoolean(name, b);
                break;
            case int i:
                writer.WriteNumber(name, i);
                break;
            case long l:
                writer.WriteNumber(name, l);
                break;
            case double d:
                writer.WriteNumber(name, d);
                break;
            case decimal dec:
                writer.WriteNumber(name, dec);
                break;
            case Guid g:
                writer.WriteString(name, g);
                break;
            case DateTime dt:
                writer.WriteString(name, dt);
                break;
            case DateTimeOffset dto:
                writer.WriteString(name, dto);
                break;
            case Enum e:
                writer.WriteString(name, e.ToString());
                break;
            default:
                writer.WriteString(name, value.ToString());
                break;
        }
    }

    private static string ToCanonical(string typeName)
    {
        // Convert PascalCase type names to lower-case kebab tokens that fit
        // the {context}.{event}.vN convention. Agent → agent, OrderLine → orderline.
        return typeName.ToLowerInvariant();
    }

    private readonly record struct PendingAudit(EntityEntry Entry, string Action);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Error,
            Message = "Kun.Audit writer failed for {Action} on {ResourceType} {ResourceId}. Swallowing exception — set KunAuditOptions.SwallowWriterExceptions=false to surface.")]
        public static partial void WriterFailed(
            ILogger logger,
            string action,
            string resourceType,
            string? resourceId,
            Exception ex);
    }
}
