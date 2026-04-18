namespace Kun.Core.Domain;

/// <summary>
/// 聚合根标记接口（非泛型）。用于框架内部通过 <c>ChangeTracker</c> 按类型筛选。
/// </summary>
public interface IAggregateRoot
{
    /// <summary>聚合根产生的领域事件集合。</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>清空已发布的领域事件（由 UoW 在持久化后调用）。</summary>
    void ClearDomainEvents();
}

/// <summary>带强类型 Key 的聚合根标记接口。</summary>
public interface IAggregateRoot<out TKey> : IAggregateRoot
    where TKey : notnull
{
    TKey Id { get; }
}
