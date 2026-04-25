namespace Kun.Core.MultiTenancy;

/// <summary>
/// <see cref="ITenantProvisioner"/> 的 no-op 默认实现，用于 Shared 策略或不需要租户生命周期
/// 钩子的宿主。两个方法都立即返回 <see cref="Task.CompletedTask"/>。
/// </summary>
public sealed class NullTenantProvisioner : ITenantProvisioner
{
    /// <summary>Singleton 实例；该类型无状态。</summary>
    public static NullTenantProvisioner Instance { get; } = new();

    /// <summary>允许 DI 容器将其作为 Singleton 构造。</summary>
    public NullTenantProvisioner() { }

    /// <inheritdoc />
    public Task ProvisionAsync(TenantInfo tenant, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task DeprovisionAsync(string tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
