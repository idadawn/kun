namespace Kun.Core.MultiTenancy;

/// <summary>
/// 租户生命周期钩子：开户（<see cref="ProvisionAsync"/>）与注销（<see cref="DeprovisionAsync"/>）。
/// </summary>
/// <remarks>
/// <para>
/// Shared 策略不需要具体实现 —— 新租户只是多一行 <c>TenantId</c> 的业务记录。消费者只需要注册
/// <see cref="NullTenantProvisioner"/>（或依赖框架提供的 Singleton 默认值）。
/// </para>
/// <para>
/// SchemaPerTenant 与 DatabasePerTenant 策略依赖本接口的非空实现：在租户开户时分别创建 schema
/// 或独立数据库，并在该隔离容器内运行 EF Core migrations。实现必须是**幂等的** ——
/// 使用 <c>CREATE SCHEMA IF NOT EXISTS</c> / <c>CREATE DATABASE IF NOT EXISTS</c> 语义；
/// 重复调用不得产生副作用。
/// </para>
/// <para>
/// 调用时机由业务方控制；框架不做自动编排。典型场景：租户 signup API 成功创建租户记录后，
/// 同事务（或补偿事务）里调用 <see cref="ProvisionAsync"/>；租户注销走反向流程。
/// </para>
/// </remarks>
public interface ITenantProvisioner
{
    /// <summary>为指定租户准备持久化基础件（schema / database / seed 数据）。幂等。</summary>
    Task ProvisionAsync(TenantInfo tenant, CancellationToken cancellationToken = default);

    /// <summary>清理指定租户的持久化基础件。幂等：对未开户的租户调用不得抛异常。</summary>
    Task DeprovisionAsync(string tenantId, CancellationToken cancellationToken = default);
}
