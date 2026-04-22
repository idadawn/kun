using Kun.Audit.Abstractions;
using Kun.Audit.DependencyInjection;
using Kun.Core.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kun.Audit.EntityFrameworkCore.DependencyInjection;

/// <summary>Service-collection extensions that wire up Kun.Audit's EF Core adapter.</summary>
public static class KunAuditEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="AuditSaveChangesInterceptor"/> so consumers
    /// can attach it to their <c>DbContextOptionsBuilder.AddInterceptors</c>
    /// call. Does **not** replace the <see cref="IAuditWriter"/>
    /// registration on its own — pair this with
    /// <see cref="UseEntityFramework{TDbContext}"/> to persist audits via
    /// a specific <see cref="DbContext"/>.
    /// </summary>
    public static KunAuditBuilder AddKunAuditEntityFrameworkCore(this KunAuditBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<ISystemClock>(SystemClock.Instance);
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();
        return builder;
    }

    /// <summary>
    /// Replaces the registered <see cref="IAuditWriter"/> with
    /// <see cref="EfAuditWriter{TDbContext}"/>, causing audit entries to
    /// be appended to <typeparamref name="TDbContext"/>'s
    /// <c>DbSet&lt;AuditLog&gt;</c> and committed in the same transaction
    /// as the business change.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core context that owns the <c>AuditLog</c> set and has <see cref="AuditLogConfiguration"/> applied in its <c>OnModelCreating</c>.</typeparam>
    public static KunAuditBuilder UseEntityFramework<TDbContext>(this KunAuditBuilder builder)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<IAuditWriter>();
        builder.Services.AddScoped<IAuditWriter, EfAuditWriter<TDbContext>>();
        return builder;
    }
}
