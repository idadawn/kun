# SaaS 多租户策略选型指南

> Kun Framework 通过 `Kun.Core.MultiTenancy` + `Kun.EntityFrameworkCore.MultiTenancy` 提供三种 SaaS 数据隔离策略：**Shared** / **SchemaPerTenant** / **DatabasePerTenant**。本文档帮你在合规、运维成本、租户规模三个维度上做出取舍，并给出各自的落地配方。
>
> 参考：[`docs/architecture/adr-p2d-multitenancy.md`](../architecture/adr-p2d-multitenancy.md) · 白皮书 §12.2

---

## TL;DR 选型决策树

```
租户数 > 数百?
├─ 是 → 合规需要"单租户数据导出"?
│       ├─ 是 → SchemaPerTenant
│       └─ 否 → Shared
└─ 否 → 单租户数据量 > 数百 GB 或强合规（金融/医疗/GDPR 数据主权）?
        ├─ 是 → DatabasePerTenant
        └─ 否 → SchemaPerTenant（或 Shared 若对合规无硬要求）
```

## 对比表

| 维度 | **Shared**（共享库） | **SchemaPerTenant**（独立 Schema） | **DatabasePerTenant**（独立库） |
|---|---|---|---|
| **隔离层级** | 应用层（`TenantId` 列 + 全局查询过滤器）+ PostgreSQL RLS 兜底 | 同库不同 schema | 完全独立的数据库实例 |
| **典型租户规模** | 数千到数十万 | 几百到几千 | 最多数百 |
| **单租户数据量** | 小（< 几十 GB） | 中（几十 GB - 几百 GB） | 任意 |
| **合规友好度** | 普通 B2B SaaS | 支持"单租户导出"（pg_dump schema） | 强合规（数据主权、独立备份/RPO/RTO） |
| **运维成本** | 最低（单一数据库） | 中（schema 数量线性增长）| 高（数据库数量线性增长，secrets 管理） |
| **迁移执行时机** | App 启动时 `Database.MigrateAsync` | 租户开户时 `ITenantProvisioner.ProvisionAsync` | 租户开户时 `ITenantProvisioner.ProvisionAsync` |
| **实现基础件** | `AddKunTenantRls()` + `IMultiTenant` 全局过滤器（已落地 Sprint 1） | `AddKunMultiTenancy<T>(o => o.Strategy = SchemaPerTenant)` + `ITenantDbContextFactory<T>`（Part 2）| `AddKunMultiTenancy<T>(o => o.Strategy = DatabasePerTenant)` + `IDbConnectionResolver`（Part 3） |
| **跨租户查询** | 原生支持（`IgnoreQueryFilters`）| 不支持（需要单独的 `AdminDbContext` 枚举 schema）| 不支持（需要枚举所有连接） |

---

## 策略一：Shared

### 适用场景
- 租户数量大、单租户数据量小的 B2B SaaS（协作工具、CRM、业务仪表板）
- 合规要求普通（租户隔离满足"查询过滤 + defence-in-depth"即可）

### 落地配方

```csharp
// Program.cs
builder.Services.AddKunEntityFrameworkCore();
builder.Services.AddKunTenantRls();               // 启用 RLS 拦截器
builder.Services.AddDbContext<CatalogDbContext>((sp, opts) =>
{
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
    opts.AddInterceptors(sp.GetRequiredService<TenantRlsTransactionInterceptor>());
});
```

实体实现 `IMultiTenant`：

```csharp
public class Product : AggregateRoot<Guid>, IMultiTenant
{
    public string TenantId { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    // ...
}
```

### 数据库侧 RLS 策略

```sql
-- 在 products 表上启用 RLS
ALTER TABLE products ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON products
    USING (tenant_id = current_setting('app.current_tenant_id', true));
```

`TenantRlsTransactionInterceptor` 会在每笔事务开始时执行 `set_config('app.current_tenant_id', @t, true)`，配合 PgBouncer transaction-pool 模式工作正常。

---

## 策略二：SchemaPerTenant

### 适用场景
- 租户数适中、需要"单租户逻辑导出"（`pg_dump -n <tenant>`）
- 每个租户共享同一 Postgres 实例但 schema 级隔离（兼顾合规与运维成本）

### 落地配方

```csharp
// 1. 注册策略 + 消费者自己的 ITenantDbContextFactory
builder.Services.AddKunMultiTenancy<CatalogDbContext>(o =>
{
    o.Strategy = TenantIsolationStrategy.SchemaPerTenant;
});

builder.Services.AddSingleton<ITenantDbContextFactory<CatalogDbContext>, CatalogTenantDbContextFactory>();

// 2. DbContext 在 OnConfiguring 读取当前租户，按 tenant_id 切 schema
builder.Services.AddDbContext<CatalogDbContext>((sp, opts) =>
{
    var tenantId = sp.GetRequiredService<ITenantContext>().TenantId ?? "public";
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .UseKunSchema(tenantId)                          // 切 DefaultSchema + MigrationsHistoryTable
        .ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>();
});

// 3. Consumer 自己实现 factory（~10 lines）
public sealed class CatalogTenantDbContextFactory : ITenantDbContextFactory<CatalogDbContext>
{
    private readonly string _connectionString;
    public CatalogTenantDbContextFactory(IConfiguration cfg) { _connectionString = cfg.GetConnectionString("Default")!; }
    public CatalogDbContext CreateForTenant(string tenantId)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_connectionString).UseKunSchema(tenantId).Options;
        return new CatalogDbContext(opts, new FixedTenantContext(tenantId));
    }
}

// 4. 租户开户时调 provisioner
await provisioner.ProvisionAsync(new TenantInfo("tenant-acme"));   // 自动 CREATE SCHEMA + migrations
```

`TenantAwareModelCacheKeyFactory` 确保每个租户有独立的 EF 模型缓存键（否则第一个租户的已编译模型会被第二个租户复用，schema 错位）。

### 运维注意
- 迁移无法在 app 启动时批量跑（可能还没注册所有租户），必须走 `ITenantProvisioner` 的开户钩子。
- Deprovision 默认抛 `NotSupportedException`；设 `AllowDestructiveDeprovision = true` 才会 `DROP SCHEMA ... CASCADE`。

---

## 策略三：DatabasePerTenant

### 适用场景
- 强合规（金融、医疗、GDPR 数据主权 = 不同租户数据不同地理分区）
- 单租户数据量大（单库 > 500 GB），需要独立备份/复制/RPO/RTO
- 租户数量较少（运维成本与租户数线性相关）

### 落地配方

```csharp
// 1. 注册策略（默认 resolver 从 TenantsConnectionOptions 读）
builder.Services.Configure<TenantsConnectionOptions>(
    builder.Configuration.GetSection(TenantsConnectionOptions.SectionName));
builder.Services.AddKunMultiTenancy<CatalogDbContext>(o =>
{
    o.Strategy = TenantIsolationStrategy.DatabasePerTenant;
});

// 2. Consumer factory 按 tenant 解析连接字符串
public sealed class CatalogTenantDbContextFactory : ITenantDbContextFactory<CatalogDbContext>
{
    private readonly IDbConnectionResolver _resolver;
    public CatalogTenantDbContextFactory(IDbConnectionResolver resolver) { _resolver = resolver; }
    public CatalogDbContext CreateForTenant(string tenantId)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_resolver.ResolveConnectionString(tenantId)).Options;
        return new CatalogDbContext(opts, new FixedTenantContext(tenantId));
    }
}
```

appsettings.json：
```json
{
  "Kun": {
    "MultiTenancy": {
      "Tenants": {
        "ConnectionStrings": {
          "tenant-acme":    "Host=db-acme;Database=catalog;Username=u;Password=p",
          "tenant-fabrikam":"Host=db-fabrikam;Database=catalog;Username=u;Password=p"
        }
      }
    }
  }
}
```

### 关键约束
- **数据库 *创建* 不在框架责任范围**：`DatabasePerTenantProvisioner.ProvisionAsync` 假设数据库已由运维流水线（Terraform / K8s Operator / 云 API）创建好，只跑 `Database.MigrateAsync` 拉齐 schema。如需自动 `CREATE DATABASE`，继承 `DatabasePerTenantProvisioner<T>` 覆盖 `ProvisionAsync`。
- **`DeprovisionAsync` 默认 no-op**：删除租户数据库的决策需要运维流程（连接池、复制、备份），框架不越权。
- **跨租户查询反模式**：禁止在 Command handler 里遍历所有连接做聚合查询。如需租户级管理视图，单独建一个 `AdminDbContext` 显式枚举连接池，并用不同的 `ICurrentUser` 角色守卫。

---

## 共通设计原则

1. **三种策略不混用于同一 `DbContext` 类型**：选一种注册，不要运行时切换（`IOptionsMonitor` 的 Strategy 变化不会重新连线 DI）。
2. **`ITenantContext` 始终是 scoped**：HTTP/gRPC 请求里由 `Kun.Security` 的 `JwtTenantContext` 填充；后台任务里由消费者按需注入。
3. **迁移工具链**：`dotnet ef migrations add` 需要 design-time `ITenantContext`。本仓库的 sample 使用 `DesignTimeTenantContext` 返回 `"public"` 作为占位符，避免 design-time 挂空依赖。

---

## 相关代码

- `src/Kun.Core/MultiTenancy/` — `ITenantContext`、`TenantIsolationStrategy`、`TenantInfo`、`ITenantProvisioner`、`NullTenantProvisioner`
- `src/Kun.EntityFrameworkCore/MultiTenancy/` — 全部三种策略的运行时件（`KunMultiTenancyOptions`、`ITenantDbContextFactory<T>`、`TenantAwareModelCacheKeyFactory`、`SchemaPerTenantProvisioner<T>`、`DatabasePerTenantProvisioner<T>`、`IDbConnectionResolver`、`OptionsBasedTenantConnectionResolver`、`TenantsConnectionOptions`、`KunMultiTenancyServiceCollectionExtensions`）
- `src/Kun.EntityFrameworkCore/Interceptors/TenantRlsTransactionInterceptor.cs` — Shared 策略的 RLS defence-in-depth
- `docs/architecture/adr-p2d-multitenancy.md` — 设计取舍完整 ADR
