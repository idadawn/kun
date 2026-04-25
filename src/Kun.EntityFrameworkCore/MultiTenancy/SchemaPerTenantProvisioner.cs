using System.Text.RegularExpressions;
using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kun.EntityFrameworkCore.MultiTenancy;

/// <summary>
/// <see cref="ITenantProvisioner"/> implementation for the
/// <see cref="TenantIsolationStrategy.SchemaPerTenant"/> strategy. On
/// <see cref="ProvisionAsync"/> executes <c>CREATE SCHEMA IF NOT EXISTS "{tenantId}"</c>
/// and runs EF Core migrations under that schema; on <see cref="DeprovisionAsync"/>
/// drops the schema when <see cref="KunMultiTenancyOptions.AllowDestructiveDeprovision"/>
/// is enabled, else throws.
/// </summary>
/// <remarks>
/// Idempotent by construction: both <c>IF NOT EXISTS</c> (provision) and <c>IF EXISTS</c>
/// (deprovision) are baked into the SQL. Tenant IDs are validated for safe schema-name
/// characters (letters, digits, underscore, hyphen) before being interpolated — invalid
/// IDs throw <see cref="ArgumentException"/> with a remediation message rather than
/// reaching the database.
/// </remarks>
/// <typeparam name="TContext">The KunDbContext-derived context whose migrations to apply.</typeparam>
public sealed partial class SchemaPerTenantProvisioner<TContext> : ITenantProvisioner
    where TContext : DbContext
{
    private static readonly Regex SafeSchemaName = MyRegex();

    private readonly ITenantDbContextFactory<TContext> _factory;
    private readonly KunMultiTenancyOptions _options;
    private readonly ILogger<SchemaPerTenantProvisioner<TContext>> _logger;

    /// <summary>Creates a new provisioner.</summary>
    public SchemaPerTenantProvisioner(
        ITenantDbContextFactory<TContext> factory,
        IOptions<KunMultiTenancyOptions> options,
        ILogger<SchemaPerTenantProvisioner<TContext>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        _factory = factory;
        _options = options.Value;
        _logger = logger ?? NullLogger<SchemaPerTenantProvisioner<TContext>>.Instance;
    }

    /// <inheritdoc />
    public async Task ProvisionAsync(TenantInfo tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ValidateSchemaName(tenant.TenantId);

        await using var context = _factory.CreateForTenant(tenant.TenantId);
        LogProvisioning(_logger, tenant.TenantId);

        // Create the schema on the server before running migrations so the migrations-history
        // table lands inside the tenant's schema (matches UseKunSchema contract).
        // Schema name is validated above; interpolation is safe.
        var createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS \"{tenant.TenantId}\"";
        await context.Database.ExecuteSqlRawAsync(createSchemaSql, cancellationToken).ConfigureAwait(false);

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeprovisionAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateSchemaName(tenantId);

        if (!_options.AllowDestructiveDeprovision)
        {
            throw new NotSupportedException(
                "Deprovisioning is disabled. Set KunMultiTenancyOptions.AllowDestructiveDeprovision = true " +
                "to enable DROP SCHEMA CASCADE. This is a data-loss operation and must be an explicit opt-in.");
        }

        await using var context = _factory.CreateForTenant(tenantId);
        LogDeprovisioning(_logger, tenantId);

        var dropSchemaSql = $"DROP SCHEMA IF EXISTS \"{tenantId}\" CASCADE";
        await context.Database.ExecuteSqlRawAsync(dropSchemaSql, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateSchemaName(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId must be a non-empty, non-whitespace string.", nameof(tenantId));
        }

        if (!SafeSchemaName.IsMatch(tenantId))
        {
            throw new ArgumentException(
                $"TenantId '{tenantId}' contains characters unsafe for a Postgres schema name. " +
                "Allowed: ASCII letters, digits, underscore, hyphen. Length 1-63.",
                nameof(tenantId));
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "SchemaPerTenantProvisioner: provisioning tenant {TenantId}.")]
    private static partial void LogProvisioning(ILogger logger, string tenantId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "SchemaPerTenantProvisioner: DROP SCHEMA for tenant {TenantId} (AllowDestructiveDeprovision=true).")]
    private static partial void LogDeprovisioning(ILogger logger, string tenantId);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_\-]{0,62}$")]
    private static partial Regex MyRegex();
}
