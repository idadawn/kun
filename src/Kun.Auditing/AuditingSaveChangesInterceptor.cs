using System.Diagnostics;
using Kun.Auditing.Events;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kun.Auditing;

/// <summary>
/// EF Core <see cref="ISaveChangesInterceptor"/> that emits one
/// <see cref="AuditEventIntegrationEvent"/> per tracked change of an entity type registered
/// via <c>services.AddAuditable&lt;TEntity&gt;()</c>. The event is forwarded to
/// <see cref="IAuditSink"/> (default: outbox writer) so it commits atomically with the
/// ambient <c>SaveChangesAsync</c> transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>AOT guarantee</b>: membership is decided purely by <see cref="IAuditableEntityRegistry"/>
/// — <c>entry.Entity.GetType()</c> is matched against a <see cref="System.Collections.Frozen.FrozenSet{T}"/>
/// of types. No runtime reflection over <see cref="AuditableAttribute"/> is performed.
/// </para>
/// <para>
/// Scoped lifetime — the interceptor resolves the scoped <see cref="AuditContext"/> and
/// writes to a scoped <see cref="IAuditSink"/>.
/// </para>
/// </remarks>
public sealed partial class AuditingSaveChangesInterceptor : ISaveChangesInterceptor
{
    private readonly IAuditableEntityRegistry _registry;
    private readonly IAuditSink _sink;
    private readonly AuditContext _auditContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuditingSaveChangesInterceptor> _logger;

    /// <summary>Creates a new <see cref="AuditingSaveChangesInterceptor"/>.</summary>
    public AuditingSaveChangesInterceptor(
        IAuditableEntityRegistry registry,
        IAuditSink sink,
        AuditContext auditContext,
        TimeProvider timeProvider,
        ILogger<AuditingSaveChangesInterceptor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(auditContext);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _registry = registry;
        _sink = sink;
        _auditContext = auditContext;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<AuditingSaveChangesInterceptor>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var context = eventData.Context;
        if (context is null)
        {
            return result;
        }

        // Snapshot before WriteAsync — the default OutboxAuditSink writes a new
        // OutboxMessage entry to the same DbContext, which mutates ChangeTracker.Entries()
        // and trips InvalidOperationException on the live enumerator.
        var auditable = context.ChangeTracker
            .Entries()
            .Where(ShouldAudit)
            .ToList();

        foreach (var entry in auditable)
        {
            var auditEvent = BuildEvent(entry);
            await _sink.WriteAsync(auditEvent, cancellationToken).ConfigureAwait(false);
            LogEmitted(_logger, auditEvent.ResourceType, auditEvent.Action);
        }

        return result;
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var context = eventData.Context;
        if (context is null)
        {
            return result;
        }

        var auditable = context.ChangeTracker
            .Entries()
            .Where(ShouldAudit)
            .ToList();

        foreach (var entry in auditable)
        {
            var auditEvent = BuildEvent(entry);
            // IAuditSink is async-first; block briefly on the sync path. The default
            // outbox sink is non-blocking (in-memory enqueue) so this is effectively
            // synchronous in production; custom sinks that perform I/O must provide a
            // non-blocking implementation of WriteAsync.
            _sink.WriteAsync(auditEvent, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            LogEmitted(_logger, auditEvent.ResourceType, auditEvent.Action);
        }

        return result;
    }

    private bool ShouldAudit(EntityEntry entry)
    {
        if (entry.State is Microsoft.EntityFrameworkCore.EntityState.Detached
            or Microsoft.EntityFrameworkCore.EntityState.Unchanged)
        {
            return false;
        }

        return _registry.RegisteredTypes.Contains(entry.Entity.GetType());
    }

    private AuditEventIntegrationEvent BuildEvent(EntityEntry entry)
    {
        var resourceType = entry.Entity.GetType().Name;
        var action = entry.State.ToString();
        var resourceId = TryGetPrimaryKey(entry);
        var traceId = _auditContext.TraceId ?? Activity.Current?.TraceId.ToString();

        return new AuditEventIntegrationEvent(
            TenantId: null,
            UserId: null,
            ActorType: _auditContext.ActorType,
            ActorId: _auditContext.ActorId,
            Action: action,
            ResourceType: resourceType,
            ResourceId: resourceId,
            Outcome: "success",
            TraceId: traceId,
            Metadata: null,
            IpAddress: _auditContext.IpAddress,
            UserAgent: _auditContext.UserAgent,
            CreatedAt: _timeProvider.GetUtcNow());
    }

    private static string? TryGetPrimaryKey(EntityEntry entry)
    {
        var pk = entry.Metadata.FindPrimaryKey();
        if (pk is null)
        {
            return null;
        }

        var parts = new List<string>(pk.Properties.Count);
        foreach (var property in pk.Properties)
        {
            var value = entry.Property(property.Name).CurrentValue;
            if (value is null)
            {
                continue;
            }
            parts.Add(value.ToString() ?? string.Empty);
        }

        return parts.Count == 0 ? null : string.Join('|', parts);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "AuditingSaveChangesInterceptor: emitted audit event for {ResourceType} action={Action}.")]
    private static partial void LogEmitted(ILogger logger, string? resourceType, string action);
}
