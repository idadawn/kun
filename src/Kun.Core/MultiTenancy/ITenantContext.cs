namespace Kun.Core.MultiTenancy;

/// <summary>
/// 租户上下文，从当前请求的认证信息（S2S JWT 的 tenant_id claim）中解析。
/// <para>
/// 框架内所有需要按租户隔离的组件（EF Core 全局过滤器、缓存 Key、消息路由）
/// 都应依赖此接口而非具体实现，以便在不同宿主（HTTP、gRPC、后台任务）中替换。
/// </para>
/// </summary>
public interface ITenantContext
{
    /// <summary>当前请求的租户 ID；后台任务中可能为 <see langword="null"/>。</summary>
    string? TenantId { get; }

    /// <summary>当前应用是否启用了多租户。</summary>
    bool IsMultiTenant { get; }

    /// <summary>打开租户作用域；释放后恢复原上下文。旧实现须显式支持后台回灌。</summary>
    IDisposable BeginScope(string? tenantId) =>
        throw new NotSupportedException("This tenant context does not support scopes. Register AsyncLocalTenantContext.");
}
