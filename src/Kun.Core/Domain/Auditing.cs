namespace Kun.Core.Domain;

/// <summary>标记实体支持软删除。由基础设施层在持久化时挂载全局查询过滤器。</summary>
public interface ISoftDelete
{
    /// <summary><see langword="true"/> when the entity has been soft-deleted.</summary>
    bool IsDeleted { get; }
    /// <summary>UTC timestamp when the entity was soft-deleted; <see langword="null"/> if not deleted.</summary>
    DateTime? DeletedAt { get; }
}

/// <summary>通用审计接口，记录创建/修改时间。</summary>
public interface IAuditedObject
{
    /// <summary>UTC timestamp when the entity was created.</summary>
    DateTime CreatedAt { get; }
    /// <summary>UTC timestamp of the last update; <see langword="null"/> if never updated.</summary>
    DateTime? UpdatedAt { get; }
}

/// <summary>
/// 带用户标识的审计接口。
/// <typeparamref name="TUserKey"/> 可以是 <see cref="Guid"/>、<see langword="string"/>、<see langword="long"/> 等，
/// 以兼容本地用户、外部 IdP 的 <c>sub</c>、数据库自增等不同场景。
/// </summary>
public interface IAuditedObject<TUserKey> : IAuditedObject
{
    /// <summary>Identity of the user who created the entity.</summary>
    TUserKey? CreatedBy { get; }
    /// <summary>Identity of the user who last updated the entity.</summary>
    TUserKey? UpdatedBy { get; }
}

/// <summary>标记实体支持多租户隔离。由 <c>KunDbContext</c> 自动附加 <c>TenantId</c> 全局过滤器。</summary>
public interface IMultiTenant
{
    /// <summary>Tenant identifier for the current entity; <see langword="null"/> in single-tenant hosts.</summary>
    string? TenantId { get; }
}
