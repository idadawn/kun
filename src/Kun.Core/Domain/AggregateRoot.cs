namespace Kun.Core.Domain;

/// <summary>
/// DDD 聚合根基类。
/// <para>
/// 聚合根是领域一致性边界的持有者，所有外部修改必须通过聚合根的公开方法进行。
/// 持久化细节（ORM 属性、表映射）一律不在此类出现——它们属于基础设施层的职责。
/// </para>
/// </summary>
/// <typeparam name="TKey">主键类型。</typeparam>
/// <typeparam name="TUserKey">审计用户 ID 类型，支持 <see cref="Guid"/>、<see langword="string"/>、<see langword="long"/>。</typeparam>
public abstract class AggregateRoot<TKey, TUserKey>
    : Entity<TKey>,
      IAggregateRoot<TKey>,
      ISoftDelete,
      IAuditedObject<TUserKey>
    where TKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>UTC timestamp when the aggregate was created.</summary>
    public DateTime CreatedAt { get; protected set; }
    /// <summary>Identity of the user who created the aggregate.</summary>
    public TUserKey? CreatedBy { get; protected set; }

    /// <summary>UTC timestamp of the last update; <see langword="null"/> if never updated.</summary>
    public DateTime? UpdatedAt { get; protected set; }
    /// <summary>Identity of the user who last updated the aggregate.</summary>
    public TUserKey? UpdatedBy { get; protected set; }

    /// <summary><see langword="true"/> when the aggregate has been soft-deleted.</summary>
    public bool IsDeleted { get; protected set; }
    /// <summary>UTC timestamp when the aggregate was soft-deleted; <see langword="null"/> if not deleted.</summary>
    public DateTime? DeletedAt { get; protected set; }

    /// <summary>Domain events raised by this aggregate since the last <see cref="ClearDomainEvents"/> call.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>在聚合根内部产生的业务变更添加一个领域事件。</summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>由 UoW 在持久化完成后调用。</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>软删除。子类可 override 追加领域事件（如 <c>ProductDeactivatedEvent</c>）。</summary>
    public virtual void Delete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}

/// <summary>便捷重载：审计用户 ID 默认为 <see cref="Guid"/>。</summary>
public abstract class AggregateRoot<TKey> : AggregateRoot<TKey, Guid>
    where TKey : notnull
{
}
