using System.Data.Common;
using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kun.EntityFrameworkCore.Interceptors;

/// <summary>
/// Emits <c>set_config('app.current_tenant_id', @tenant, true)</c> at the start of every
/// EF Core transaction so PostgreSQL Row-Level Security policies can use
/// <c>current_setting('app.current_tenant_id')</c> to enforce tenant isolation even when a
/// caller bypasses the global query filter (e.g. <c>IgnoreQueryFilters()</c>, admin scenarios).
/// </summary>
/// <remarks>
/// <para>
/// Hooks <c>TransactionStartedAsync</c> / <c>TransactionStarted</c> rather than
/// <c>ConnectionOpenedAsync</c>: PgBouncer in transaction-pooling mode recycles the backend
/// connection at transaction boundaries, so setting a GUC outside an explicit transaction
/// would leak across tenants. Scoping to <c>SET LOCAL</c> (equivalently
/// <c>set_config(..., is_local=true)</c>) ties the setting to the current transaction.
/// </para>
/// <para>
/// Uses <c>set_config(...)</c> instead of <c>SET LOCAL app.current_tenant_id = '...'</c> because
/// Postgres's <c>SET</c> utility statement does not accept parameter binding; the function form
/// does, keeping the tenant id out of the SQL text (defence against log leakage / injection).
/// </para>
/// <para>
/// No-op when <see cref="ITenantContext.TenantId"/> is <see langword="null"/> or empty
/// (background jobs / setup scripts). RLS policies must be written to accept
/// <c>current_setting('app.current_tenant_id', true) IS NULL</c> as the unscoped path if those
/// flows need to read cross-tenant data; otherwise they must set the GUC manually.
/// </para>
/// </remarks>
public sealed partial class TenantRlsTransactionInterceptor : DbTransactionInterceptor
{
    internal const string GucName = "app.current_tenant_id";
    internal const string CommandText = "SELECT set_config('app.current_tenant_id', @tenant, true)";

    private readonly ITenantContext _tenantContext;
    private readonly ILogger<TenantRlsTransactionInterceptor> _logger;

    /// <summary>Creates a new <see cref="TenantRlsTransactionInterceptor"/>.</summary>
    public TenantRlsTransactionInterceptor(
        ITenantContext tenantContext,
        ILogger<TenantRlsTransactionInterceptor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        _tenantContext = tenantContext;
        _logger = logger ?? NullLogger<TenantRlsTransactionInterceptor>.Instance;
    }

    /// <inheritdoc/>
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            LogSkipNoTenant(_logger);
            return await base.TransactionStartedAsync(connection, eventData, result, cancellationToken).ConfigureAwait(false);
        }

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = result;
        cmd.CommandText = CommandText;
        var p = cmd.CreateParameter();
        p.ParameterName = "tenant";
        p.Value = tenantId;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return await base.TransactionStartedAsync(connection, eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            LogSkipNoTenant(_logger);
            return base.TransactionStarted(connection, eventData, result);
        }

        using var cmd = connection.CreateCommand();
        cmd.Transaction = result;
        cmd.CommandText = CommandText;
        var p = cmd.CreateParameter();
        p.ParameterName = "tenant";
        p.Value = tenantId;
        cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();

        return base.TransactionStarted(connection, eventData, result);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "TenantRlsTransactionInterceptor: skipping " + GucName + " (no tenant in context).")]
    private static partial void LogSkipNoTenant(ILogger logger);
}
