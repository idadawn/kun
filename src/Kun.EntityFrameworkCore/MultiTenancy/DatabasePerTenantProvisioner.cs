using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kun.EntityFrameworkCore.MultiTenancy;

/// <summary>
/// <see cref="ITenantProvisioner"/> for the
/// <see cref="TenantIsolationStrategy.DatabasePerTenant"/> strategy. Runs EF Core
/// migrations against the tenant's dedicated database on <see cref="ProvisionAsync"/>.
/// <see cref="DeprovisionAsync"/> is a no-op by default because dropping a tenant database
/// is an operations concern (needs admin credentials and often a separate control plane) —
/// the framework does not take that responsibility.
/// </summary>
/// <remarks>
/// <para>
/// Assumes the tenant's database has already been <strong>created</strong> at the server
/// level by an operations workflow (Terraform, K8s Operator, cloud provider API). This
/// provisioner's job is to bring the schema up-to-date via
/// <c>Database.MigrateAsync()</c>. Databases cannot be reliably created in a portable way
/// across Postgres / MySQL / SQL Server from EF Core itself, so that step is delegated.
/// </para>
/// <para>
/// For scenarios where the app owns database creation, subclass this provisioner and
/// override <see cref="ProvisionAsync"/> to run your vendor-specific <c>CREATE DATABASE</c>
/// before calling <c>base.ProvisionAsync</c>.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The KunDbContext-derived context whose migrations to apply.</typeparam>
public partial class DatabasePerTenantProvisioner<TContext> : ITenantProvisioner
    where TContext : DbContext
{
    private readonly ITenantDbContextFactory<TContext> _factory;
    private readonly ILogger<DatabasePerTenantProvisioner<TContext>> _logger;

    /// <summary>Creates a new provisioner.</summary>
    public DatabasePerTenantProvisioner(
        ITenantDbContextFactory<TContext> factory,
        ILogger<DatabasePerTenantProvisioner<TContext>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _logger = logger ?? NullLogger<DatabasePerTenantProvisioner<TContext>>.Instance;
    }

    /// <inheritdoc />
    public virtual async Task ProvisionAsync(TenantInfo tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        await using var context = _factory.CreateForTenant(tenant.TenantId);
        LogMigrating(_logger, tenant.TenantId);

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default no-op: dropping a tenant database is an operations concern that requires
    /// admin credentials and coordinated cleanup (connection pools, replication, backups).
    /// Override this method in a subclass or swap in a custom <see cref="ITenantProvisioner"/>
    /// when the app owns database lifecycle end-to-end.
    /// </remarks>
    public virtual Task DeprovisionAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        LogDeprovisionNoOp(_logger, tenantId);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "DatabasePerTenantProvisioner: running migrations for tenant {TenantId}.")]
    private static partial void LogMigrating(ILogger logger, string tenantId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "DatabasePerTenantProvisioner: DeprovisionAsync is a no-op by design (tenant {TenantId}). Override or register a custom ITenantProvisioner if the app owns database drop.")]
    private static partial void LogDeprovisionNoOp(ILogger logger, string tenantId);
}
