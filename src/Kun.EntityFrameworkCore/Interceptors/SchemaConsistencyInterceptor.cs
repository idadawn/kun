using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kun.EntityFrameworkCore.Interceptors;

/// <summary>
/// Fails fast when a <see cref="KunDbContext"/>'s <c>DefaultSchema</c> diverges from the EF Core
/// <c>MigrationsHistoryTable</c> schema. Throws at the first <c>SaveChangesAsync</c> so developers
/// catch mis-wired <c>UseKunSchema</c> calls before any business write lands in the wrong schema.
/// </summary>
/// <remarks>
/// <para>
/// The check runs at most once per interceptor instance (gated by
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>). Per <c>kun-gap-closure-v1.md</c>
/// M5, the comparison cannot run at <c>OnModelCreating</c> time because the relational model is
/// not yet finalized — <c>SavingChangesAsync</c> is the earliest correct hook point.
/// </para>
/// </remarks>
public sealed class SchemaConsistencyInterceptor : ISaveChangesInterceptor
{
    private int _checked;

    /// <inheritdoc/>
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        VerifyOnce(eventData);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    /// <inheritdoc/>
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        VerifyOnce(eventData);
        return result;
    }

    private void VerifyOnce(DbContextEventData eventData)
    {
        if (Interlocked.CompareExchange(ref _checked, 1, 0) != 0)
        {
            return;
        }

        var context = eventData.Context
            ?? throw new InvalidOperationException(
                "SchemaConsistencyInterceptor.SavingChanges received a null DbContext from EF Core.");

        var defaultSchema = context.Model.GetDefaultSchema();
        if (string.IsNullOrEmpty(defaultSchema))
        {
            return;
        }

        var options = context.GetService<IDbContextOptions>();
        var relational = options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();
        var historySchema = relational?.MigrationsHistoryTableSchema;

        if (!string.Equals(defaultSchema, historySchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"KunDbContext.DefaultSchema='{defaultSchema}' does not match MigrationsHistoryTable schema='{historySchema ?? "(null)"}'. " +
                $"Call .UseKunSchema(\"{defaultSchema}\") on DbContextOptionsBuilder to wire both consistently.");
        }
    }
}
