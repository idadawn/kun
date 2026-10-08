namespace Kun.MultiTenancy.Abstractions;

/// <summary>为尚未打开显式作用域的上下文提供已认证的租户标识。</summary>
public interface ITenantIdSource
{
    /// <summary>已认证且通过白名单校验的租户标识；未认证时为空。</summary>
    string? TenantId { get; }
}
