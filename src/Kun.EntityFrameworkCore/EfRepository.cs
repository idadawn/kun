#pragma warning disable KUNLEGACY001 // 旧接口的兼容实现，不作为新代码的入口。
using Kun.Core.Domain;
using Kun.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kun.EntityFrameworkCore;

/// <summary>
/// Generic EF Core implementation of <see cref="IRepository{T}"/> for aggregate roots.
/// Persistence is committed by the Unit of Work — callers must not call
/// <c>SaveChangesAsync</c> directly.
/// </summary>
/// <typeparam name="T">The aggregate root type.</typeparam>
public class EfRepository<T> : IRepository<T>
    where T : class, IAggregateRoot
{
    /// <summary>The underlying EF Core <see cref="KunDbContext"/>.</summary>
    protected KunDbContext DbContext { get; }

    /// <summary>Creates a new <see cref="EfRepository{T}"/>.</summary>
    /// <param name="dbContext">The scoped <see cref="KunDbContext"/> instance.</param>
    public EfRepository(KunDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        DbContext = dbContext;
    }

    /// <inheritdoc/>
    public virtual async Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => await DbContext.Set<T>().FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public virtual async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return await SpecificationEvaluator
            .Apply(DbContext.Set<T>(), specification)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual async Task<int> CountAsync(
        ISpecification<T> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var query = specification.Criteria is not null
            ? DbContext.Set<T>().Where(specification.Criteria)
            : DbContext.Set<T>();
        return await query.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await DbContext.Set<T>().AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual void Update(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DbContext.Set<T>().Update(entity);
    }

    /// <inheritdoc/>
    public virtual void Remove(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DbContext.Set<T>().Remove(entity);
    }
}
