using Kun.Audit.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kun.Audit.EntityFrameworkCore;

/// <summary>
/// <see cref="IAuditWriter"/> that persists entries through a typed
/// <typeparamref name="TDbContext"/>. The context may be the same
/// <c>KunDbContext</c> that holds the business aggregates (audit rows
/// land in the same transaction) or a dedicated audit database context
/// (decoupled lifecycle, different retention policy).
/// </summary>
/// <typeparam name="TDbContext">The EF Core context that owns the <c>AuditLog</c> <c>DbSet</c>.</typeparam>
/// <remarks>
/// <para>
/// Does **not** call <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.
/// When invoked from inside <see cref="AuditSaveChangesInterceptor"/>,
/// the outer EF <c>SaveChanges</c> commits the audit rows atomically
/// with the business change. When invoked standalone (e.g. from a
/// Mediator pipeline behavior) the caller is responsible for flushing.
/// </para>
/// </remarks>
public sealed class EfAuditWriter<TDbContext> : IAuditWriter
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    /// <summary>Creates a new writer bound to <paramref name="dbContext"/>.</summary>
    public EfAuditWriter(TDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _dbContext.Set<AuditLog>().Add(entry);
        return Task.CompletedTask;
    }
}
