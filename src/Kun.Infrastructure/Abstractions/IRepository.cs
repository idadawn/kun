using Kun.Core.Domain;

namespace Kun.Infrastructure.Abstractions;

/// <summary>
/// 聚合根仓储抽象。业务层依赖本接口，不依赖具体 ORM。
/// </summary>
public interface IRepository<T> where T : class, IAggregateRoot
{
    Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    void Update(T entity);

    void Remove(T entity);
}

/// <summary>
/// 工作单元抽象。通常每个请求作用域一个实例，<see cref="SaveChangesAsync"/> 由 Pipeline Behavior 统一触发。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// 提交所有聚合根变更，并在同一事务内写入 Outbox 消息。
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
