using System.Data.Common;
using System.Text.RegularExpressions;
using Kun.Core.MultiTenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kun.EntityFrameworkCore.Interceptors;

/// <summary>
/// Defence-in-depth interceptor that pushes the current <see cref="ITenantContext.TenantId"/>
/// into the PostgreSQL session variable <c>app.current_tenant_id</c> on every
/// connection open, and clears it on connection close. Used in combination with
/// Row-Level Security policies on the database side — EF Core's global query
/// filter remains the first line of defence; this interceptor is the second.
/// </summary>
/// <remarks>
/// <para>
/// Only activates when the underlying <see cref="DbConnection"/> is an Npgsql
/// connection; for other providers (MySQL, SQL Server) the interceptor is a
/// no-op, because the <c>SET app.current_tenant_id = ...</c> syntax is
/// PostgreSQL-specific. The check is nominal (namespace-prefix on the connection
/// type) so this module can remain free of a direct Npgsql package dependency.
/// </para>
/// <para>
/// SQL injection guard: <c>SET</c> statements in PostgreSQL cannot be parameterised,
/// so the tenant id is inlined into the command text. We therefore refuse to emit
/// the command unless the value matches the strict whitelist
/// <see cref="SafeTenantIdPattern"/>. A business tenant id that does not match
/// (spaces, quotes, semicolons, etc.) will throw instead of being silently coerced
/// — fail loudly rather than hand a crafted value to <c>SET</c>.
/// </para>
/// <para>
/// Registered as Scoped by <see cref="DependencyInjection.KunEntityFrameworkCoreServiceCollectionExtensions.AddKunEntityFrameworkCore"/>;
/// callers opt in by adding the interceptor to their <c>DbContextOptionsBuilder</c>:
/// <code>
/// services.AddDbContext&lt;MyDbContext&gt;((sp, opts) =&gt;
/// {
///     opts.UseNpgsql(connectionString);
///     opts.AddInterceptors(sp.GetRequiredService&lt;TenantRlsConnectionInterceptor&gt;());
/// });
/// </code>
/// </para>
/// </remarks>
public sealed partial class TenantRlsConnectionInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// Whitelist for tenant-id values allowed inside <c>SET app.current_tenant_id = '...'</c>.
    /// ASCII letters, digits, hyphen, underscore, dot; 1..128 chars. Covers GUIDs,
    /// slugs, and short numeric ids while rejecting any value that could escape
    /// the single-quoted literal.
    /// </summary>
    public const string SafeTenantIdPattern = "^[A-Za-z0-9._-]{1,128}$";

    private static readonly Regex SafeTenantId = new(SafeTenantIdPattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ITenantContext _tenantContext;
    private readonly ILogger<TenantRlsConnectionInterceptor> _logger;

    /// <summary>Initializes a new instance of the interceptor.</summary>
    public TenantRlsConnectionInterceptor(
        ITenantContext tenantContext,
        ILogger<TenantRlsConnectionInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (!IsPostgres(connection))
        {
            return;
        }

        ExecuteSync(connection, BuildSetOrReset(_tenantContext.TenantId));
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (!IsPostgres(connection))
        {
            return;
        }

        await ExecuteAsync(
            connection,
            BuildSetOrReset(_tenantContext.TenantId),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (IsPostgres(connection))
        {
            TryExecuteReset(connection);
        }

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (IsPostgres(connection))
        {
            await TryExecuteResetAsync(connection).ConfigureAwait(false);
        }

        return result;
    }

    private string BuildSetOrReset(string? tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            return "RESET app.current_tenant_id";
        }

        if (!SafeTenantId.IsMatch(tenantId))
        {
            throw new InvalidOperationException(
                $"TenantRlsConnectionInterceptor refuses to set app.current_tenant_id to " +
                $"an unsafe value (must match regex {SafeTenantIdPattern}). " +
                "Either tighten the source that produces TenantId or disable the interceptor " +
                "for this context.");
        }

        return $"SET app.current_tenant_id = '{tenantId}'";
    }

    private void TryExecuteReset(DbConnection connection)
    {
        try
        {
            ExecuteSync(connection, "RESET app.current_tenant_id");
        }
        catch (Exception ex)
        {
            Log.ResetFailed(_logger, ex);
        }
    }

    private async ValueTask TryExecuteResetAsync(DbConnection connection)
    {
        try
        {
            await ExecuteAsync(connection, "RESET app.current_tenant_id", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.ResetFailed(_logger, ex);
        }
    }

    private static void ExecuteSync(DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static bool IsPostgres(DbConnection connection)
    {
        var name = connection.GetType().FullName;
        return name is not null &&
            (name.StartsWith("Npgsql.", StringComparison.Ordinal) ||
             name.Equals("Npgsql.NpgsqlConnection", StringComparison.Ordinal));
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Warning,
            Message = "Failed to RESET app.current_tenant_id on connection close. " +
                "Next request on this pooled connection may inherit a stale tenant scope.")]
        public static partial void ResetFailed(ILogger logger, Exception ex);
    }
}
