namespace Kun.Core.MultiTenancy;

/// <summary>
/// SaaS 多租户数据隔离策略。由 <c>Kun.EntityFrameworkCore.MultiTenancy</c> 层在运行时解析成
/// 具体的 DbContext 构造、迁移调度与连接解析行为。
/// </summary>
/// <remarks>
/// Part 1（2026-04-23）只落地枚举本身 + 配套契约类型；<see cref="SchemaPerTenant"/> 与
/// <see cref="DatabasePerTenant"/> 的运行时实现分别延后到 P2-D Part 2 / Part 3。
/// 详细取舍见 <c>docs/architecture/adr-p2d-multitenancy.md</c>。
/// </remarks>
public enum TenantIsolationStrategy
{
    /// <summary>
    /// 所有租户同库同 schema，用 <c>IMultiTenant</c> 实体的 <c>TenantId</c> 列 + 全局查询
    /// 过滤器做应用层隔离，叠加 PostgreSQL RLS（<c>AddKunTenantRls()</c>）做 defence-in-depth。
    /// 适用：租户数量大但单租户数据量小、合规要求普通的 B2B SaaS。
    /// </summary>
    Shared = 0,

    /// <summary>
    /// 同库不同 schema，每个租户有独立 schema。逻辑隔离更强、备份粒度到租户；需要运行时
    /// 按 <c>ITenantContext.TenantId</c> 切 <c>DefaultSchema</c> 与 <c>MigrationsHistoryTable</c>。
    /// 适用：合规要求中等（如数据可"单租户导出"）、租户数量 &lt;= 数千。
    /// </summary>
    SchemaPerTenant = 1,

    /// <summary>
    /// 每租户独立数据库实例（通常也是独立连接字符串、独立备份、独立 RPO/RTO）。
    /// 适用：合规要求强（医疗/金融 GDPR 数据主权）、租户数量 &lt;= 数百但单租户数据量大。
    /// </summary>
    DatabasePerTenant = 2,
}
