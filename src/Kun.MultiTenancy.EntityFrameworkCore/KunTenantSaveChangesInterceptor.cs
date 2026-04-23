using Kun.Core.Domain;
using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kun.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that walks newly added
/// <see cref="IMultiTenant"/> entities and, when their
/// <see cref="IMultiTenant.TenantId"/> is <see langword="null"/> or
/// empty, injects the ambient <see cref="ITenantContext.TenantId"/>.
/// Existing non-empty values are preserved so administrative tooling
/// that explicitly sets a tenant id retains control.
/// </summary>
public sealed partial class KunTenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<KunTenantSaveChangesInterceptor> _logger;

    /// <summary>Creates a new interceptor bound to the supplied tenant context.</summary>
    public KunTenantSaveChangesInterceptor(
        ITenantContext tenantContext,
        ILogger<KunTenantSaveChangesInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            InjectTenantIds(context);
        }
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            InjectTenantIds(context);
        }
        return result;
    }

    private void InjectTenantIds(DbContext context)
    {
        var tenant = _tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenant))
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<IMultiTenant>())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            var existing = entry.Entity.TenantId;
            if (!string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            // The property setter may be `protected` on aggregate roots;
            // the PropertyEntry API writes through the EF metadata and
            // respects that shape without runtime reflection into the type.
            entry.Property(nameof(IMultiTenant.TenantId)).CurrentValue = tenant;
            Log.TenantAutoFilled(_logger, entry.Entity.GetType().Name, tenant);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
            Message = "Kun.MultiTenancy auto-filled TenantId on {EntityType}: {TenantId}")]
        public static partial void TenantAutoFilled(ILogger logger, string entityType, string tenantId);
    }
}
