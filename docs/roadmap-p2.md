# Kun Framework — Phase 2 候选清单（v1.5 乘风破浪）

> 基于白皮书 `docs/whitepaper.md` §12.2 的 5 条 Phase-2 路标，把每一条拆成可立项的 kickoff 依据：**作用域 / 可交付物 / 前置依赖 / 风险 / 工作量 / kickoff PRD 草稿**。
> 最近更新：2026-04-23。

## TL;DR — 推荐开工顺序

| 排名 | 赛道 | 推荐理由 | 风险 | 工作量 |
|:---:|:---|:---|:---:|:---:|
| 1 | **P2-D SaaS 多租户完整方案** | `ITenantContext` + `TenantRlsTransactionInterceptor` 基础件已落地；Phase-2 需横向扩展为 3 种可选隔离策略（shared / schema-per-tenant / database-per-tenant）；白皮书 §12.2 明确列为 v1.5 承诺 | Low | M |
| 2 | **P2-C YARP 网关增强**（gRPC-Web + 分布式限流策略库） | `Kun.Gateway` 已有 YARP + ASP.NET Core RateLimit 骨架；把策略抽成可插拔的策略库、补上 gRPC-Web 转换即可 | Low | S–M |
| 3 | **P2-E Saga 可视化监控面板** | `SagaInstance` 表已存在；加 read-only REST + 最小 Razor/Blazor 仪表板，无新基础设施 | Low–Med | M |
| 4 | **P2-A gRPC 无状态服务 AOT + 性能基准** | AOT 候选模块矩阵已就绪，主要是 `BenchmarkDotNet` + `Kun.Sample` gRPC 服务端的 AOT 基线验证 | Low | S |
| 5 | **P2-B Proto 源生成器** | 从 `.proto` 自动生成 CQRS 胶水（Command/Query/Handler 骨架）；需要独立 Roslyn SG 工程、模板库、设计决策多 | **High** | L |

**建议先开 P2-D**：收益最高（直接满足 v1.5 白皮书承诺），风险最低（在现有 multi-tenant 抽象上横向扩展），上下游依赖已就绪。

---

## P2-A — gRPC 无状态服务完整 Native AOT 支持 + 性能基准

### 作用域
- 把 `Kun.Sample.Catalog` 的 gRPC endpoint（或新增 `Kun.Sample.Grpc`）纳入 CI 的 `dotnet publish -p:PublishAot=true` 矩阵，确认零警告。
- 基于 `BenchmarkDotNet` 建立对比基准：JIT vs AOT 冷启动时间、p99 RPC 延迟、内存占用。
- 产出 `docs/benchmarks/grpc-aot.md`，含复现脚本与 CI 工件归档路径。

### 可交付物
1. `benchmarks/Kun.Benchmarks.Grpc/` 新工程，含 unary/server-streaming 两类基准。
2. `.github/workflows/benchmarks.yml`（或 append to `ci.yml`），每周定时 + PR 标签触发。
3. AOT 启动时间对比表（目标：AOT 冷启动 <100ms）。
4. `docs/benchmarks/grpc-aot.md` 方法学 + 结果文档。

### 前置依赖
- `Kun.Grpc` 已 AOT 候选（✅ 已落地）。
- `Kun.Sample.AppHost` Aspire 编排可复用做宿主（✅ 已落地）。
- Linux runner 上的 AOT publish（✅ CI 已有 ubuntu-latest 矩阵）。

### 风险
- **Low**：`BenchmarkDotNet` + AOT 的组合在 .NET 10 生态已有成熟先例；主要是写测试与沉淀文档。
- 唯一变数：AOT 下 `Grpc.AspNetCore` 是否有未暴露的 reflection 调用 —— 若发现需要 follow-up，不阻塞赛道交付。

### 工作量：**S**（~3–5 eng-days）

### Kickoff PRD 草稿
```json
{
  "task": "P2-A: gRPC AOT 基准与回归保护",
  "stories": [
    {"id":"US-P2A-1","title":"新增 Kun.Benchmarks.Grpc 工程并接入 BenchmarkDotNet"},
    {"id":"US-P2A-2","title":"CI 矩阵：Kun.Sample gRPC 服务端 AOT publish 零警告"},
    {"id":"US-P2A-3","title":"docs/benchmarks/grpc-aot.md：方法学 + JIT/AOT 对比结果 + 复现脚本"}
  ]
}
```

---

## P2-B — Proto 源生成器：从 `.proto` 自动生成 CQRS 胶水代码

### 作用域
- 新增 `src/Kun.Grpc.SourceGenerator/`（Roslyn incremental SG），扫描标注 `[KunProto]` 的 `.proto` 生成文件，为每个 `rpc` 自动生成：
  - `I{Method}Request : IRequest<{Method}Response>` 或等价 CQRS 契约；
  - gRPC service base 的 override 转发到 `ISender.Send`；
  - `Result<T>` 到 proto response 的映射模板。
- 可选：`[KunCommand("order.create.v1")]` / `[KunQuery]` 自定义 proto option 决定生成 Command 还是 Query。

### 可交付物
1. `Kun.Grpc.SourceGenerator` 分析器工程（AOT-compatible 可选，SG 不进入运行时）。
2. 单元测试：输入 `.proto` → 期望生成源代码（`Verify.SourceGenerators`）。
3. `samples/Kun.Sample.Catalog` 从手工胶水改为 SG 驱动，对比前后 LOC。
4. `docs/guides/proto-source-generator.md` 设计文档（触发规则、自定义 option、AOT 约束）。

### 前置依赖
- `Kun.Mediator` 已有 `ISender` + handler 注册 API（✅）。
- `Kun.Grpc` 已有 `KunResultToRpcInterceptor`（✅）。
- `[DynamicallyAccessedMembers]` / `[RequiresUnreferencedCode]` 约束已融入项目（✅）。

### 风险
- **High**：Roslyn SG 设计空间大；多个决策点需讨论（命名约定、自定义 option 语法、部分类 vs 全量生成、Protobuf-net 兼容性）。
- Proto → C# 类型映射与 CQRS 请求/响应语义对齐需要 ADR。

### 工作量：**L**（~10–15 eng-days）

### Kickoff PRD 草稿（需先走 ralplan deliberate 模式做 ADR）
```json
{
  "task": "P2-B: .proto 驱动的 CQRS 胶水源生成器（deliberate-mode）",
  "stories": [
    {"id":"US-P2B-0","title":"Architect + Critic: Proto↔CQRS 映射规则 ADR"},
    {"id":"US-P2B-1","title":"SG 基础骨架 + Verify.SourceGenerators 测试"},
    {"id":"US-P2B-2","title":"Command/Query 分流（proto option 或约定）"},
    {"id":"US-P2B-3","title":"Kun.Sample.Catalog 迁移样例 + LOC 对比数据"}
  ]
}
```

---

## P2-C — YARP 网关增强（gRPC-Web + 分布式限流策略库）

### 作用域
- **gRPC-Web 转换**：在 `Kun.Gateway` 增加 `UseGrpcWeb()` 扩展，保证浏览器直连后端 gRPC 可行；
- **分布式限流策略库**：把现有 ASP.NET Core RateLimit 的"固定窗口/滑动窗口/令牌桶"封装成 `IKunRateLimitPolicy`，支持多维度 key（IP / 租户 / 用户 / 路由），Redis 后端存储共享计数器。
- 产出策略样例 + benchmarks。

### 可交付物
1. `Kun.Gateway.GrpcWeb` 命名空间 + `MapKunGrpcWeb()` 扩展方法。
2. `Kun.Gateway.RateLimit` 策略抽象：`IKunRateLimitPolicy` / `IKunRateLimitKeyResolver`；
3. 内置 3 个策略：`FixedWindowPerTenant`、`SlidingWindowPerUser`、`TokenBucketPerRoute`。
4. Redis 后端实现 `RedisRateLimitStore`（`INCR + EXPIRE` 原子序列）。
5. 集成测试：Testcontainers Redis + 并发压测验证限流精度。
6. `docs/guides/gateway-rate-limit.md` + `docs/guides/grpc-web.md` 两份文档。

### 前置依赖
- `Kun.Gateway` 已 AOT 候选（✅）。
- `HybridCache` / Redis 客户端（✅ 已在 `Directory.Packages.props`）。
- YARP 2.x（✅）。

### 风险
- **Low**：增量扩展；限流算法在 .NET 生态有成熟先例；Redis-backed 计数器已在 `IProcessedMessageStore` 验证过。
- gRPC-Web 的 CORS 与鉴权链路需要配合 S2S JWT 验证端点调整（小）。

### 工作量：**S–M**（~5–8 eng-days）

### Kickoff PRD 草稿
```json
{
  "task": "P2-C: Kun.Gateway gRPC-Web + 分布式限流策略库",
  "stories": [
    {"id":"US-P2C-1","title":"IKunRateLimitPolicy + 3 个内置策略 + Redis 后端"},
    {"id":"US-P2C-2","title":"MapKunGrpcWeb + 与 S2sAuthTransform 联动验证"},
    {"id":"US-P2C-3","title":"Testcontainers 并发限流精度测试（100 并发 / 误差 < 2%）"},
    {"id":"US-P2C-4","title":"docs/guides/gateway-rate-limit.md + gateway/grpc-web.md"}
  ]
}
```

---

## P2-D — SaaS 多租户完整方案（共享库 / 独立 Schema / 独立库 三种策略）⭐ 推荐首选

### 作用域
白皮书 §12.2 明示 v1.5 要提供**三种隔离策略**。目前仅有 schema-per-service（单租户在服务间分库的架构），与 SaaS 多租户的 schema-per-tenant 概念正交，需要横向扩展：
1. **Shared-library（共享库）**：所有租户同库同 schema，用 `TenantId` 列 + RLS（已落地 `TenantRlsTransactionInterceptor`）；
2. **Schema-per-tenant（独立 Schema）**：同库不同 schema，`UseKunSchema(tenantId)` 动态拼接；
3. **Database-per-tenant（独立库）**：每租户独立连接字符串，`IDbConnectionResolver` 按 tenant 分发。

### 可交付物
1. `ITenantIsolationStrategy` 抽象 + 3 个实现。
2. `AddKunMultiTenancy(Strategy.Shared | .SchemaPerTenant | .DatabasePerTenant, …)` 统一 DI 入口。
3. `ITenantDbContextFactory<TContext>` 用于 schema/database-per-tenant 场景下的 scoped context 构造。
4. 三份迁移运行手册 + sample：`Kun.Sample.SaaS.Shared` / `Kun.Sample.SaaS.SchemaPerTenant` / `Kun.Sample.SaaS.DatabasePerTenant`。
5. `docs/guides/saas-multitenancy.md` 策略选型决策树（数据量/合规/运维成本 trade-off）。
6. 集成测试：每种策略覆盖 "tenant A 读不到 tenant B" 的红线用例。

### 前置依赖
- `ITenantContext` + `IMultiTenant` 全局过滤器（✅）。
- `TenantRlsTransactionInterceptor`（✅ Sprint 1 已落地）。
- `UseKunSchema` + `SchemaConsistencyInterceptor`（✅ P0-3 已落地）。
- `KunDbContext` 可继承（✅）。

### 风险
- **Low**：Schema-per-tenant 与 Database-per-tenant 都是 EF Core 成熟模式；最大难点是 DI 作用域 + 迁移脚本编排，已在内部 plan 反复讨论过。
- 需要明确 EF Core migrations 在三种策略下的执行时序（app 启动 / 租户注册 / 租户首次写入）。

### 工作量：**M**（~8–12 eng-days）

### 拆分为 Part 1 / Part 2 / Part 3

ADR: [`docs/architecture/adr-p2d-multitenancy.md`](architecture/adr-p2d-multitenancy.md)

| Part | 状态 | 范围 |
|:---:|:---:|:---|
| **Part 1** | ✅ **已落地** (2026-04-23) | ADR + 前瞻类型：`TenantIsolationStrategy` 枚举、`TenantInfo` 记录、`ITenantProvisioner` 接口 + `NullTenantProvisioner` 默认实现。Shared 策略现有 `AddKunTenantRls()` 保持不变（YAGNI，详见 ADR D1）。|
| **Part 2** | ✅ **已落地** (2026-04-23) | `SchemaPerTenant` 策略运行时：`ITenantDbContextFactory<TContext>`、`TenantAwareModelCacheKeyFactory`（按 `KunDbContext.TenantIdForModelCacheKey` 拆分模型缓存）、`SchemaPerTenantProvisioner<TContext>`（`CREATE SCHEMA IF NOT EXISTS` + `Database.MigrateAsync`，幂等）、`AddKunMultiTenancy<TContext>` DI 入口（Shared + SchemaPerTenant，DatabasePerTenant 在解析时抛 `NotImplementedException`）。PgBouncer transaction-pool 集成测试仍延后 —— 本机无 Docker；CI 上与 Part 3 一并落。|
| **Part 3** | ✅ **已落地** (2026-04-23) | `DatabasePerTenant` 策略：`IDbConnectionResolver` + `OptionsBasedTenantConnectionResolver` 默认实现（读 `TenantsConnectionOptions`）+ `DatabasePerTenantProvisioner<TContext>`（`ProvisionAsync` 跑 `Database.MigrateAsync`；`DeprovisionAsync` 默认 no-op，下放给运维/cloud provider。数据库创建 CREATE DATABASE 明确下放给运维工作流，框架不负责）。`AddKunMultiTenancy<TContext>` DI 分支替换旧 `NotImplementedException` 为真实注册。|
| — | ⏳ 跨 Part | `Kun.Sample.SaaS.Shared` / `SchemaPerTenant` / `DatabasePerTenant` 三份样例、`docs/guides/saas-multitenancy.md` 决策树、三套 Testcontainers 红线用例。这些随 Part 2 / Part 3 各自上线。|

### 下一步

Part 2 kickoff 推荐单独起 ralplan（`IModelCacheKeyFactory` 的模型缓存键 + EF migrations 时序是需要 consensus 的决策），完成后再滚 Part 3。

---

## P2-E — Saga 可视化监控面板

### 作用域
- 新增 `Kun.Saga.Dashboard`（Razor Pages 或 Blazor Server），只读 UI：
  - 运行中 Saga 列表（按状态过滤：Running / Compensating / Compensated / Completed / Failed）；
  - 单个 Saga 详情页：时间线 + 每步 Execute/Compensate 耗时 + 当前 context payload（脱敏）；
  - 超时实例告警表格；
  - 手动重放（opt-in，管理员角色）。
- 基础数据来源：现有 `SagaInstance` + Outbox 表；零新表。

### 可交付物
1. `src/Kun.Saga.Dashboard/` 新工程（AOT-candidate 待评估：Blazor Server 通常非 AOT 候选）。
2. `UseKunSagaDashboard()` 扩展方法，挂载到 `/kun/sagas`；
3. 基础权限集成：只读要 `SagaViewer` 角色，重放要 `SagaAdmin` 角色；
4. OpenTelemetry 埋点：每次查看/重放都产生一个 audit event；
5. Testcontainers 集成测试：验证"新建 Saga → dashboard 可见 → 补偿 → 状态更新"闭环。
6. `docs/observability/saga-dashboard.md`。

### 前置依赖
- `SagaInstance` 表与实体（✅）。
- `Kun.Security` 角色/claim 校验（✅）。
- `Kun.Auditing` 可记录 dashboard 操作（✅）。

### 风险
- **Low–Med**：Blazor Server 与 .NET 10 Minimal API 共存的托管模式需要选型（同宿主 vs 独立 sidecar）；
- UI 设计工作量（即使 MVP）容易膨胀，需要严格限定"只读 + 一个重放按钮"的 MVP 边界。

### 工作量：**M**（~7–10 eng-days）

### Kickoff PRD 草稿
```json
{
  "task": "P2-E: Kun.Saga.Dashboard（只读 + 单按钮重放）",
  "stories": [
    {"id":"US-P2E-1","title":"Razor Pages 骨架 + UseKunSagaDashboard 扩展"},
    {"id":"US-P2E-2","title":"Saga 列表 + 详情页（按状态过滤）"},
    {"id":"US-P2E-3","title":"SagaViewer / SagaAdmin 角色 + 审计埋点"},
    {"id":"US-P2E-4","title":"Testcontainers 闭环测试"},
    {"id":"US-P2E-5","title":"docs/observability/saga-dashboard.md"}
  ]
}
```

---

## 不在 P2 范围

白皮书 §12.3 的 Phase 3（v2.0 翱翔云端）条目不在本阶段讨论：
- K8s/Azure Container Apps 部署模板；
- Service Mesh（Istio / Linkerd）mTLS 与 S2S JWT 协同；
- EF Core 完整 AOT 支持（等官方 Provider 就绪）；
- 多区域部署与跨区域事件复制。

Phase 1 收尾的非代码动作（NuGet 首次发版、链路追踪手工走查、分支保护等）仍在 `docs/roadmap.md` 跟踪，与 P2 并行推进。

---

## 下一步动作

1. **用户回复 "选 X"**（X ∈ {A, B, C, D, E}）—— 锁定首个 P2 赛道；
2. 对该赛道运行 `ralplan --deliberate`（P2-B 必须 deliberate；其余按需），产出 `docs/shared/agent-tiers.md` 对齐的正式 ADR；
3. ADR 通过后切 ralph/autopilot 进入实现阶段。
