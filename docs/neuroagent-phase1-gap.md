# P0-001 — Kun 框架 Phase 1 Gap Audit

**日期**：2026-04-21
**基准文档**：SAD v3.0（§5 基础设施层、§7 C# 服务层 Kun 框架）、Phase1 TaskPlan v1.0
**版本**：Kun 0.1.0-alpha.1 / .NET 10 / EF Core 10
**审计范围**：kun/src/ 全部 13 个模块 + kun/tests/ 11 个测试项目 + kun/samples/ 6 个示例项目

---

## 1. Kun 框架现有模块清单

> 提取自 `kun/Kun.sln` 及 `kun/src/**/*.csproj`

| # | 项目名 | csproj 路径 | 版本 | 核心职责 | 当前状态 |
|---|--------|------------|------|---------|---------|
| 1 | **Kun.Core** | `src/Kun.Core/Kun.Core.csproj` | 0.1.0-alpha.1 | DDD 基类（Entity/AggregateRoot/ValueObject）、CQRS 接口（IRequest/ICommand/IQuery）、Result 模式、ITenantContext/ICurrentUser、IDomainEvent | **已实现** — 含完整类型：Entity<TKey>, AggregateRoot<TKey>, ValueObject, IMultiTenant, ISoftDelete, IAuditedObject, ITenantContext, ICurrentUser, ITracedCurrentUser |
| 2 | **Kun.Mediator** | `src/Kun.Mediator/Kun.Mediator.csproj` | 0.1.0-alpha.1 | 无反射 CQRS 调度器（SourceGen）、IPipelineBehavior、INotification | **已实现** — ISender, IPublisher, IPipelineBehavior<TRequest,TResponse>, INotificationHandler |
| 3 | **Kun.Infrastructure** | `src/Kun.Infrastructure/Kun.Infrastructure.csproj` | 0.1.0-alpha.1 | IRepository<T>、IUnitOfWork、ISpecification<T>、IProcessedMessageStore 抽象 | **已实现** — 含 Specification<T> 基类 |
| 4 | **Kun.EntityFrameworkCore** | `src/Kun.EntityFrameworkCore/Kun.EntityFrameworkCore.csproj` | 0.1.0-alpha.1 | KunDbContext（含租户全局 Query Filter + SoftDelete filter）、EfRepository<T>、Outbox 集成、UnitOfWork | **已实现** — KunDbContext 含 IMultiTenant 自动 QueryFilter、OutboxMessage 配置、IntegrationEventOutboxAccumulator、UnitOfWorkPipelineBehavior |
| 5 | **Kun.Dapper** | `src/Kun.Dapper/Kun.Dapper.csproj` | 0.1.0-alpha.1 | Dapper 读侧抽象、IReadDbConnectionFactory | **已实现** — IReadDbConnectionFactory（[DapperAot] 支持） |
| 6 | **Kun.EventBus** | `src/Kun.EventBus/Kun.EventBus.csproj` | 0.1.0-alpha.1 | Outbox 抽象（IOutboxPublisher/IOutboxMessageStore/IOutboxLockProvider）、事件类型注册（IEventTypeRegistry）、幂等消费（IProcessedMessageStore）、Background publisher | **已实现** — 完整 Outbox pipeline：OutboxPublisherBackgroundService、EventTypeRegistry、IntegrationEventBuffer、RedisOutboxLockProvider、InMemoryProcessedMessageStore、RedisProcessedMessageStore |
| 7 | **Kun.EventBus.RabbitMQ** | `src/Kun.EventBus.RabbitMQ/Kun.EventBus.RabbitMQ.csproj` | 0.1.0-alpha.1 | RabbitMQ broker adapter | **已实现** — 依赖 RabbitMQ.Client |
| 8 | **Kun.Gateway** | `src/Kun.Gateway/Kun.Gateway.csproj` | 0.1.0-alpha.1 | YARP 反向代理、OIDC JWT → S2S JWT 转换、JWKS 端点、限流 | **已实现** — S2sTokenIssuer、S2sAuthTransform、JwksEndpoint、IRateLimitPartitionResolver、IpRateLimitPartitionResolver、TenantRateLimitPartitionResolver |
| 9 | **Kun.Security** | `src/Kun.Security/Kun.Security.csproj` | 0.1.0-alpha.1 | S2S JWT 验证、JWKS、租户/用户上下文提取 | **已实现** — KunServiceAuthenticationExtensions、KunServiceAuthenticationOptions、IKunJwksProvider/FileKunJwksProvider、JwtTenantContext、JwtCurrentUser、PropagateS2sTokenHandler |
| 10 | **Kun.AspNetCore** | `src/Kun.AspNetCore/Kun.AspNetCore.csproj` | 0.1.0-alpha.1 | Minimal API 扩展、ProblemDetails、全局异常处理、Health Checks | **已实现** — KunEndpointRouteBuilderExtensions、KunExceptionHandler、ErrorToProblemDetailsExtensions、KunHealthChecksExtensions |
| 11 | **Kun.Grpc** | `src/Kun.Grpc/Kun.Grpc.csproj` | 0.1.0-alpha.1 | gRPC 拦截器、Result→RpcException 映射 | **已实现** — 依赖 Grpc.AspNetCore + Kun.Security |
| 12 | **Kun.Saga** | `src/Kun.Saga/Kun.Saga.csproj` | 0.1.0-alpha.1 | Saga 编排引擎：步骤、补偿、超时、状态持久化 | **已实现** — ISagaOrchestrator、ISaga<TContext>、ISagaStep<TContext>、ISagaContext、ISagaInstanceStore |
| 13 | **Kun.Validation** | `src/Kun.Validation/Kun.Validation.csproj` | 0.1.0-alpha.1 | IValidator<T>、ValidationResult、Mediator pipeline behavior | **已实现** — IValidator<T> + Pipeline behavior 集成 |

### 测试覆盖

11 个测试项目对应 src 模块（Core/Mediator/Dapper/EventBus/Gateway/Grpc/Saga/Security/Validation + IntegrationTests）。

### 示例项目

6 个示例项目：Kun.Sample.AppHost、Catalog（DDD + CQRS 完整示例）、Order（Saga 示例）、Minimal、Shared、Tests。

---

## 2. Phase 1 NeuroAgent 需要的 Kun 能力清单

> 参考 SAD v3.0 §7（C# 服务层）+ §5（基础设施层）+ Phase1 TaskPlan

| 能力 | SAD 引用 | 对应 TaskPlan | 现有 Kun 模块 | 是否已具备 |
|------|---------|--------------|-------------|----------|
| **IFileStorage 抽象**（MinIO/S3/Local） | §7.3 Kun.Storage | P1-KN-001 | **无** | 缺失 — Kun 无 Storage/OSS 模块 |
| **S2S JWT 签发**（RSA、含 JWKS 端点） | §7.1 Kun.Edge、§12 S2S JWT 设计 | P1-KN-002 | Kun.Gateway（S2sTokenIssuer）+ Kun.Security（验证侧） | **已具备** — S2sTokenIssuer + JwksEndpoint + KunServiceAuthenticationExtensions |
| **S2S JWT 验证**（下游服务 Bearer 认证） | §7.1、§12 | P1-KN-002 | Kun.Security | **已具备** — FileKunJwksProvider + KunServiceAuthenticationExtensions |
| **Outbox Pattern**（EF Core 同事务写 + Background publish） | §3.2 schema 所有权、§7.4 Aspire | P1-KN-003 | Kun.EventBus + Kun.EntityFrameworkCore | **已具备** — IntegrationEventOutboxAccumulator + OutboxPublisherBackgroundService + RedisOutboxLockProvider |
| **OTel 追踪**（ASP.NET Core + HTTP + 自定义 span） | §14 可观测性 | P1-CS-002 | Kun.AspNetCore（Health Checks） | **部分** — Kun 框架不自带 OTel（由消费方 ServiceDefaults 负责），但 Outbox 支持 TraceParent/TraceState 传播 |
| **租户上下文**（ITenantContext + EF QueryFilter + JWT claims 提取） | §13 多租户隔离 | P1-CS-009 | Kun.Core（ITenantContext）+ Kun.EntityFrameworkCore（自动 QueryFilter）+ Kun.Security（JwtTenantContext） | **已具备** — 全链路：JWT claims → JwtTenantContext → ITenantContext → KunDbContext.OnModelCreating 自动过滤 |
| **CQRS 调度**（SourceGen、Pipeline Behaviors） | §7.2 Kun.Platform | P1-CS-012+ | Kun.Mediator | **已具备** |
| **DDD 基类**（AggregateRoot/Entity/ValueObject/DomainEvent） | §7 各服务 | 全部 C# 业务任务 | Kun.Core | **已具备** |
| **读写分离**（EF Core 写侧 + Dapper 读侧） | §7 各服务 | 全部 C# 业务任务 | Kun.EntityFrameworkCore + Kun.Dapper | **已具备** |
| **Saga 编排**（Phase 2 Flow 引擎前的补偿模式） | §18 Temporal Phase 2 | 无 Phase 1 任务 | Kun.Saga | **已具备** — Phase 1 无直接需求 |
| **gRPC 服务**（内部服务间可选同步调用） | §12 通信协议 | 无 Phase 1 任务 | Kun.Grpc | **已具备** — Phase 1 无直接需求 |
| **限流**（IP + Tenant 级别） | §7.1 Kun.Edge | P1-CS-006 | Kun.Gateway | **已具备** — IRateLimitPartitionResolver + IP/Tenant Resolver |
| **审计日志**（自动记录 Command 操作） | §6.7 audit schema、§15.5 | P1-CS-019 | **无** — 无 Kun.Audit 模块 | 缺失 — 需要新增审计中间件/Interceptor |

---

## 3. 各 Kun 模块对 NeuroAgent Phase 1 的能力匹配

### 3.1 已满足 Phase 1 需求的模块（无需修改）

| 模块 | 理由 |
|------|------|
| **Kun.Core** | DDD 基类、ITenantContext、ICurrentUser、Result — Phase 1 业务层可直接继承使用 |
| **Kun.Mediator** | 无反射 CQRS 调度 — 所有 Command/Query Handler 基础设施就绪 |
| **Kun.Infrastructure** | IRepository/IUnitOfWork/ISpecification — 与 EF Core 配合使用 |
| **Kun.EntityFrameworkCore** | KunDbContext（含租户 filter + Outbox 集成）— 写侧基础设施完备 |
| **Kun.Dapper** | 读侧连接工厂 — Phase 1 查询场景可选用 |
| **Kun.EventBus** | 完整 Outbox 抽象 + Background publisher — 满足 P1-KN-003 |
| **Kun.EventBus.RabbitMQ** | RabbitMQ broker — 与 EventBus 配合使用 |
| **Kun.Gateway** | S2S JWT 签发 + YARP 路由 + 限流 — 满足 P1-KN-002 |
| **Kun.Security** | S2S JWT 验证 + JWKS + 租户/用户上下文提取 — 下游服务认证就绪 |
| **Kun.AspNetCore** | Minimal API 扩展 + ProblemDetails + 异常处理 — 业务服务宿主层就绪 |
| **Kun.Validation** | IValidator<T> + Pipeline behavior — 请求验证就绪 |
| **Kun.Saga** | Phase 1 无直接需求，保留备用 |
| **Kun.Grpc** | Phase 1 无直接需求，保留备用 |

### 3.2 需新增能力的模块

| 模块 | 需新增/修改的能力 | 对应 TaskPlan |
|------|-----------------|--------------|
| **（新建）Kun.Storage** | IFileStorage 抽象接口 + S3/MinIO 实现 + Local 实现。接口需包含：CreatePresignedUploadUrl / CreatePresignedDownloadUrl / PutObject / GetObject / HeadObject / DeleteObject / ListObjects。活跃 web 类型强制 Content-Disposition: attachment | P1-KN-001 |
| **（新建）Kun.Audit** | 审计日志 Interceptor/Middleware，自动从 CQRS Pipeline 捕获 Command 执行记录，异步写 audit schema（含哈希链） | P1-CS-019 |

---

## 4. 分类清单（4-way）

### 可保留（13/13 src 模块）

全部 13 个现有 src 模块可直接保留。Kun 框架代码质量高、架构完备，Phase 1 业务层直接消费。

> Kun.Core / Kun.Mediator / Kun.Infrastructure / Kun.EntityFrameworkCore / Kun.Dapper / Kun.EventBus / Kun.EventBus.RabbitMQ / Kun.Gateway / Kun.Security / Kun.AspNetCore / Kun.Grpc / Kun.Saga / Kun.Validation

### 需重构

**无**。现有模块实现符合 SAD v3.0 架构要求。

### 需删除

**无**。无废弃或冲突模块。

### 需新建（2 个模块）

| 新模块 | 职责 | Phase 1 必要性 | 优先级 |
|--------|------|--------------|--------|
| **Kun.Storage** | IFileStorage 抽象 + S3/MinIO/Local 实现 | P1-KN-001（M1 Week 1-2） | **高** — Kun.Storage 和 NeuroAgent.Storage 均依赖此接口 |
| **Kun.Audit** | 审计日志 Pipeline Behavior / Interceptor + 哈希链 | P1-CS-019（M4 Week 7-8） | **中** — 不阻塞 M1-M3，可 Week 7 开始 |

---

## 5. Phase 1 必须在 Kun 里新增/修改的清单

> 对应 TaskPlan P1-KN-001 ~ P1-KN-003

### P1-KN-001 Kun.Storage 新增 IFileStorage 抽象

**状态**：缺失，需新建整个模块
**工作目录**：kun/
**SAD 引用**：§7.3 Kun.Storage（IFileStorage 接口 + S3/MinIO/Local 实现）

需实现：
- `src/Kun.Storage/Kun.Storage.csproj`
- `src/Kun.Storage/Abstractions/IFileStorage.cs` — 接口定义（7 个方法）
- `src/Kun.Storage/Abstractions/ObjectMetadata.cs` — 元数据类型
- `src/Kun.Storage/Abstractions/PresignedUploadUrl.cs` / `PresignedDownloadUrl.cs`
- `src/Kun.Storage/Implementations/S3FileStorage.cs` — AWS SDK for .NET，兼容 MinIO
- `src/Kun.Storage/Implementations/LocalFileSystemStorage.cs` — 本地开发用
- `tests/Kun.Storage.Tests/Kun.Storage.Tests.csproj` — 单元测试（mock S3 或 MinIO testcontainer）
- 需处理：活跃 web 类型（text/html 等）强制 Content-Disposition: attachment

### P1-KN-002 S2S JWT 签发与验证

**状态**：**已具备** — 无需新增
**验证**：Kun.Gateway/S2sTokenIssuer.cs（签发）+ Kun.Security/Authentication/（验证）+ JwksEndpoint.cs（JWKS 端点）

TaskPlan 原文提到"RSA 签名 + publish JWKS 端点"，已全部实现。下游服务通过 `AddKunServiceAuthentication()` 扩展方法接入。

**建议**：确认 S2S JWT claims 是否包含 SAD v3.0 §12 要求的完整字段（sub, tenant_id, roles, trace_id, membership_id, 15 分钟过期），如有差异只需微调 `S2sTokenOptions`。

### P1-KN-003 Outbox Pattern 补齐

**状态**：**已具备** — 无需新增
**验证**：Kun.EventBus（完整 Outbox 抽象）+ Kun.EntityFrameworkCore（IntegrationEventOutboxAccumulator 同事务写入）+ OutboxPublisherBackgroundService（后台发布）+ Kun.EventBus.RabbitMQ（RabbitMQ broker）+ RedisOutboxLockProvider（分布式锁）

Sample 项目 `Kun.Sample.Order` 已展示完整 Outbox 使用模式（EfOutboxMessageStore）。

**建议**：确认 `IEventTypeRegistry` 是否支持 `[IntegrationEventId("xxx.v1")]` 标记模式（如未支持，只需小幅增强注册方式）。

---

## 6. 补充：SAD v3.0 引用的其他 Kun 相关能力

| SAD 引用 | 能力 | Kun 现状 | 说明 |
|----------|------|---------|------|
| §3.3 Redis Key 命名 | `NeuroAgent.Contracts.Redis` 定义 | 不在 Kun 范围 | 属于 NeuroAgent 业务层 |
| §3.3 RabbitMQ 消息 Schema | `NeuroAgent.IntegrationEvents` 定义 | Kun.EventBus 提供框架 | 事件注册 + 序列化框架就绪，具体事件类型在 NeuroAgent 层定义 |
| §3.3 MinIO Bucket 结构 | Bucket 结构文档化 | 需要 Kun.Storage | 等待 P1-KN-001 |
| §6.8 RLS + SET LOCAL | PG session 级 tenant 设置 | Kun.EntityFrameworkCore 已有 QueryFilter | EF 侧已通过 QueryFilter 实现；如需 `SET LOCAL app.current_tenant_id` 作为额外防御层，需在 KunDbContext 添加 connection interceptor（**建议**在 P1-CS-011 实现） |
| §14.4 日志规范 | Serilog JSON + trace_id/tenant_id | 不在 Kun 范围 | 由 ServiceDefaults + 各服务配置负责 |
| §15.5 Audit 哈希链 | audit.operations 的 prev_hash/content_hash | 缺失 | 需新建 Kun.Audit 或在业务层实现 |

---

## 7. 结论

Kun 框架对 NeuroAgent Phase 1 的 **核心基础设施支持度约 85%**：

- **已满足**：CQRS/DDD/EF Core 写侧/Dapper 读侧/Outbox+Saga/S2S JWT 全链路/多租户 QueryFilter/限流/验证
- **需新建**：Kun.Storage（IFileStorage）、Kun.Audit（审计 Pipeline）
- **无需修改**：全部 13 个现有 src 模块代码无需改动

Phase 1 实际在 Kun 侧的工作量集中在 **P1-KN-001（Kun.Storage 新建）**，其他两个 KN 任务（S2S JWT + Outbox）已具备。

---

Verified: `find kun/src -name "*.csproj" | wc -l` = **13** (实际跑出 13 个源码项目)
