namespace Kun.Core.MultiTenancy;

/// <summary>
/// 不可变的租户身份元信息，作为 <see cref="ITenantProvisioner"/> 等生命周期 API 的入参。
/// </summary>
public sealed record TenantInfo
{
    /// <summary>租户 ID。</summary>
    public string TenantId { get; }

    /// <summary>可选的展示名（UI / 日志 / 审计用途）。null 表示消费者未提供。</summary>
    public string? DisplayName { get; init; }

    /// <summary>创建一个新的 <see cref="TenantInfo"/>。</summary>
    /// <param name="tenantId">租户 ID。必须非 null、非空、非全空白字符。</param>
    /// <param name="displayName">可选的展示名；默认 <see langword="null"/>。</param>
    /// <exception cref="ArgumentException">当 <paramref name="tenantId"/> 为 null / 空字符串 / 仅空白字符时抛出。</exception>
    public TenantInfo(string tenantId, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("TenantId must be a non-empty, non-whitespace string.", nameof(tenantId));
        }

        TenantId = tenantId;
        DisplayName = displayName;
    }
}
