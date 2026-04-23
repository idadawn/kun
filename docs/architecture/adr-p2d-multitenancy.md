# ADR: P2-D SaaS 多租户 3-策略架构

**Status**: Part 1 Accepted (2026-04-23) · Part 2 Accepted (2026-04-23) · Part 3 Deferred
**Phase**: 2 （白皮书 §12.2 乘风破浪）
**Scope**: `Kun.Core.MultiTenancy` + `Kun.EntityFrameworkCore.MultiTenancy`（未来）
**Supersedes**: 无（增量于 P0-3 schema-per-service、P1-4 `TenantRlsTransactionInterceptor`、既有 `ITenantContext`）

---

## Context

白皮书 §12.2 承诺 v1.5 提供 **SaaS 多租户完整方案（共享库 / 独立 Schema / 独立库 三种隔离策略）**。当前 (2026-04-23 `develop`) 已有的多租户基础件是：

| 组件 | 位置 | 用途 |
|---|---|---|
| `ITenantContext` | `src/Kun.Core/MultiTenancy/ITenantContext.cs` | 抽象，返回 `TenantId` / `IsMultiTenant` |
| `NullTenantContext` | 同上 | 非多租户宿主或后台任务 |
| `JwtTenantContext`（非本 ADR 范围） | `Kun.Security` | HTTP/gRPC 请求时从 S2S JWT claim 抽 |
| `IMultiTenant` 全局过滤器 | `KunDbContext.OnModelCreating` | 对 `IMultiTenant` 实体自动追加 `e.TenantId == ctx.TenantId` 过滤 |
| `TenantRlsTransactionInterceptor` | `src/Kun.EntityFrameworkCore/Interceptors/` | 每笔事务开启时 `SELECT set_config('app.current_tenant_id', @t, true)`，用 Postgres RLS 做 defence-in-depth |
| `AddKunTenantRls()` 扩展 | `src/Kun.EntityFrameworkCore/DependencyInjection/KunEntityFrameworkCoreServiceCollectionExtensions.cs` | 消费者显式开启 RLS 拦截器 |

**现实情况**：上面的组合 = **Shared 策略（所有租户同库同 schema，用 `TenantId` 列 + RLS 双层防御）**。它已落地并通过 Sprint 1 集成测试。

Phase 2 要补的是两条**真正的新策略**：
- **Schema-per-tenant**：同库不同 schema，每个 tenant 有独立 schema；
- **Database-per-tenant**：每 tenant 独立连接字符串与独立数据库实例。

两者都需要新的运行时基础件：DbContext 实例化工厂、模型缓存键、迁移调度、连接解析。

---

## Decision

### D1. Shared 策略 = 现有 `AddKunTenantRls` + `IMultiTenant` 全局过滤器（不新加包装 API）

**不引入** `AddKunMultiTenancy(Strategy=Shared)` 之类的包装扩展来"统一入口"。现有 `AddKunTenantRls()` 已经是惯用、已公开 API；再套一层 strategy-flag 会让消费者在两个完全等价的入口之间选择。这是 YAGNI 原则 —— 多策略 API 只有在 2+ 策略同时落地时才有实际价值。

Part 2（SchemaPerTenant）落地时再评估是否需要新增 `AddKunMultiTenancy(Strategy=…)` 作为 "multi-entry dispatcher"；届时 Shared 路径可以委托到现有 `AddKunTenantRls` 而不破坏任何现有 consumer。

### D2. `TenantIsolationStrategy` 枚举 NOW 落地（forward-looking 类型）

即使当前只有 Shared 可用，`Kun.Core.MultiTenancy.TenantIsolationStrategy { Shared, SchemaPerTenant, DatabasePerTenant }` 枚举在 Part 1 就落地。理由：

- 消费者选型文档需要这个稳定的词汇表（`docs/guides/saas-multitenancy.md` 的决策树）；
- 未来 `ITenantProvisioner` / `ITenantDbContextFactory` 签名会引用它；
- 枚举是零运行成本的前瞻 API，不会增加维护负担。

Part 2 / Part 3 未交付前，`SchemaPerTenant` / `DatabasePerTenant` 值有"定义但未实现"的语义。文档显式声明。

### D3. DbContext 实例化模型（按策略分叉）

| 策略 | DbContext 作用域 | 模型缓存键 |
|---|---|---|
| Shared | Scoped（现状，EF Core 默认） | 默认 `IModelCacheKeyFactory`（按 Type 键） |
| SchemaPerTenant（Part 2） | Scoped + **需要 `ITenantDbContextFactory<TContext>`** 在 scope 起始时用当前 `ITenantContext.TenantId` 构建正确的 `MigrationsHistoryTable.Schema` 与 `DefaultSchema` | **必须**提供自定义 `IModelCacheKeyFactory`，把 `TenantId` 并入缓存键，否则第二个租户复用第一个租户的模型 → schema 错位 |
| DatabasePerTenant（Part 3） | Scoped + `IDbConnectionResolver` 在 `OnConfiguring` 时用 `ITenantContext.TenantId` 解析连接字符串 | 默认 `IModelCacheKeyFactory` 即可（模型结构相同，只换连接） |

### D4. 迁移调度模型（按策略分叉）

| 策略 | 迁移触发时机 | 谁来发起 |
|---|---|---|
| Shared | App 启动时 `context.Database.MigrateAsync()`（或部署流水线手工） | 宿主 |
| SchemaPerTenant | **租户开户时**通过 `ITenantProvisioner.ProvisionAsync(new TenantInfo(tenantId))` 触发；实现内部切到该租户 schema 做迁移 | `ITenantProvisioner` 实现 |
| DatabasePerTenant | **租户开户时**通过 `ITenantProvisioner.ProvisionAsync` 触发；实现先创建数据库/连接、再运行迁移 | `ITenantProvisioner` 实现 |

App-start 批量迁移对 Schema/DatabasePerTenant **不适用**（启动时可能尚未注册所有租户，或租户是运行时动态创建）。

### D5. 租户生命周期接口 `ITenantProvisioner`

Part 1 定义契约：

```csharp
public interface ITenantProvisioner
{
    Task ProvisionAsync(TenantInfo tenant, CancellationToken ct = default);
    Task DeprovisionAsync(string tenantId, CancellationToken ct = default);
}
```

- **幂等性约定**：`ProvisionAsync` 重复调用必须无副作用（用 `CREATE SCHEMA IF NOT EXISTS` / `CREATE DATABASE IF NOT EXISTS` 语义）。
- **Null 实现默认**：`NullTenantProvisioner` 两个方法都返回 `Task.CompletedTask`，让 Shared 策略的消费者不用写空实现。
- **Part 2 / Part 3 才提供非空实现**：`SchemaPerTenantProvisioner` / `DatabasePerTenantProvisioner`。

### D6. DI 生命周期规则

| 组件 | 生命周期 | 原因 |
|---|---|---|
| `ITenantContext` | Scoped | 按请求/消息绑定（现状） |
| `ITenantProvisioner` | Singleton | 无状态，仅依赖配置与日志 |
| `ITenantDbContextFactory<TContext>` (Part 2) | Scoped | 依赖 scoped `ITenantContext` |
| `IDbConnectionResolver` (Part 3) | Singleton（或 Scoped，取决于实现是否需要 scoped 依赖） | 默认 Singleton |

---

## Alternatives Considered

### A1. "为 Shared 加一个 `AddKunMultiTenancy(Strategy=Shared)` 包装入口，与 Part 2/3 对齐"

**拒绝**：YAGNI。现有 `AddKunTenantRls` 已经是稳定 API，加包装只会让消费者选择困扰。Part 2 / Part 3 要上线时 `AddKunMultiTenancy` 才有 2+ 策略可选，那时再评估。

### A2. "用特性/反射自动发现 `ITenantProvisioner` 实现"

**拒绝**：违反本仓库 `CLAUDE.md` 的 AOT-first 原则；显式 DI 注册与强约定足够清晰。

### A3. "SchemaPerTenant 通过动态 `SET search_path` 替代 `DefaultSchema`"

**拒绝（推迟到 Part 2 再讨论）**：`search_path` 切换的事务语义与 RLS + PgBouncer 交互复杂，需要 Part 2 集成测试验证；此时先在 ADR 记录为 open question。

### A4. "Database-per-tenant 在单一 `DbContext` 实例内通过 `Database.SetConnectionString` 运行时切换"

**拒绝（推迟到 Part 3 再讨论）**：EF Core 对运行时切连接的模型缓存语义不稳定；更可能的路径是 `ITenantDbContextFactory` 每次从池里按 tenant 取一个已绑定连接的实例。

---

## Consequences

### Positive

- Shared 策略消费者零破坏：现有 `AddKunTenantRls` 签名与行为不变。
- Part 1 完全无运行时耦合：4 个新类型都是纯契约，`Kun.Core` 的零第三方依赖约束保持。
- Part 2 / Part 3 的实现者有稳定的目标契约（`TenantIsolationStrategy` 枚举 + `ITenantProvisioner` 接口）。

### Negative

- `TenantIsolationStrategy.SchemaPerTenant` / `.DatabasePerTenant` 在 Part 2 / Part 3 前属于"定义但未实现"的值。消费者若提前用会在未来策略 dispatcher 里拿到 `NotSupportedException`。Part 2 落地时这个语义会被解除。
- `ITenantProvisioner` 的生命周期钩子（开户/注销）与业务域的 tenant onboarding 流程是解耦的；消费者需要自己把这两个调用接到业务流里，框架不做自动编排。

### Neutral

- 现有 `NullTenantContext` 与新增 `NullTenantProvisioner` 的"null 对象模式"是仓库的既有风格延伸，不是新约定。

---

## Deferred to Part 2 (SchemaPerTenant)

- `ITenantDbContextFactory<TContext>` 接口定义与实现；
- 自定义 `IModelCacheKeyFactory` 把 `TenantId` 并入缓存键；
- `SchemaPerTenantProvisioner`：`CREATE SCHEMA IF NOT EXISTS "{tenantId}"` + 在该 schema 下跑 EF migrations；
- `KunDbContext` 的 `OnConfiguring` 重载，读取 scoped `ITenantContext.TenantId` → 调 `UseKunSchema`；
- PgBouncer transaction-pool 兼容性集成测试（schema 切换跨连接的副作用）。

## Deferred to Part 3 (DatabasePerTenant)

- `IDbConnectionResolver` 抽象 + 基于 `IOptionsMonitor<TenantsConnectionOptions>` 的内置实现；
- `DatabasePerTenantProvisioner`：创建数据库 + 跑 migrations + 可选 bootstrap seeding；
- 跨租户 admin 查询的反模式文档（建议走单独的 `AdminDbContext`，显式枚举所有连接）；
- 多区域部署下租户连接发现的运维模式（可选，与 Phase 3 多区域话题挂钩）。

---

## Follow-ups

1. Part 2 kickoff 单独起 ralplan（`SchemaPerTenant` 的模型缓存键与迁移时序是需要 consensus 的决策）。
2. Part 3 kickoff 之前先与运维对齐 secrets 管理（每租户一条连接字符串意味着 secret scale 线性增长）。
3. 当 Part 2 或 Part 3 任一交付时，在 `docs/roadmap-p2.md` P2-D 章节更新状态，并回补 `docs/guides/saas-multitenancy.md` 的决策树。
