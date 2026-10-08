namespace Kun.Auditing.Events;

/// <summary>规范审计事件 v2：使用字符串租户与主体，保留 v1 类型供迁移。</summary>
/// <param name="Id">与本地 AuditLog 相同的事件标识。</param>
/// <param name="TenantId">来自租户上下文的标识。</param>
/// <param name="UserId">来自已认证用户上下文的标识。</param>
/// <param name="ActorType">主体类别，支持 agent。</param>
/// <param name="ActorId">主体标识。</param>
/// <param name="Action">实体变更类型。</param>
/// <param name="ResourceType">实体类型。</param>
/// <param name="ResourceId">实体主键。</param>
/// <param name="TraceId">链路标识。</param>
/// <param name="IpAddress">请求来源地址。</param>
/// <param name="CreatedAt">审计时间。</param>
public sealed record AuditEventIntegrationEventV2(Guid Id, string? TenantId, string? UserId,
    string ActorType, string? ActorId, string Action, string ResourceType, string? ResourceId,
    string? TraceId, string? IpAddress, DateTimeOffset CreatedAt);
