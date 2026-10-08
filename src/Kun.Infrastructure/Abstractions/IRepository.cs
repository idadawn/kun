using Kun.Core.Domain;

namespace Kun.Infrastructure.Abstractions;

/// <summary>
/// 聚合根仓储抽象。业务层依赖本接口，不依赖具体 ORM。
/// </summary>
[Obsolete("旧通用仓储入口不用于新代码；请为业务定义端口并由 EF Provider 实现。", DiagnosticId = "KUNLEGACY001")]
public interface IRepository<T> where T : class, IAggregateRoot
{
    /// <inheritdoc/>
    Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    void Update(T entity);

    /// <inheritdoc/>
    void Remove(T entity);
}

/// <summary>
/// 工作单元抽象。通常每个请求作用域一个实例，<see cref="SaveChangesAsync"/> 由 Pipeline Behavior 统一触发。
/// </summary>
[Obsolete("请使用 Kun.EntityFrameworkCore.IUnitOfWork。", DiagnosticId = "KUNLEGACY001")]
public interface IUnitOfWork
{
    /// <summary>
    /// 提交所有聚合根变更，并在同一事务内写入 Outbox 消息。
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
