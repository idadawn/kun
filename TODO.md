# Kun Framework 开发 TodoList

> **使用说明**（给 Claude Code 的操作指引）
>
> - 本文件是 Kun Framework 的完整开发任务清单。按 **阶段 → 子模块 → 任务** 三级组织，任务粒度细到"完成一项即可合一个 PR"。
> - 每项任务前有一个 `- [ ]` 复选框。**完成后把空格换成 `x` 即可打勾**：`- [x]`。
> - 每个阶段的任务**严格按顺序执行**，下面的任务可能依赖上面的任务已完成。跨阶段也要按顺序。
> - 每项任务必须遵守 `.claude/skills/` 下对应技能包的约定。**开工前先让 Claude 读对应技能**。
> - 每个阶段末尾有"阶段验收"清单——阶段内所有任务完成后，验收全过才允许进入下一阶段。
> - 每次 PR 合入前，必须跑过 `dotnet format`、`dotnet test`、AOT 发布验证。
> - 本文件是**活文档**，允许在开发过程中补充子任务；但不允许跳过已列出的任务。

---

## 🏁 前置准备

- [ ] 在本地克隆仓库：`git clone https://github.com/idadawn/kun.git`
- [ ] 确认 .NET 10 SDK 已安装：`dotnet --version` 输出 `10.0.100` 或更高
- [ ] 确认 Docker Desktop 可用（Testcontainers 需要）
- [ ] 阅读 `README.md` 与 `docs/whitepaper.md`，理解框架整体愿景
- [ ] 阅读 `.claude/skills/kun-framework-conventions/SKILL.md`（总纲，所有任务的共同前置）
- [ ] 首次执行 `dotnet restore` 成功
- [ ] 首次执行 `dotnet build -c Release`（会有大量错误，因为实现代码未写——这是预期的）

---

## 🔧 贯穿性任务（每阶段都要做）

这些不属于某个具体阶段，而是**每个 PR 都要检查**的事项：

- [ ] PR 合入前运行 `dotnet format --verify-no-changes`
- [ ] PR 合入前运行 `dotnet test` 全绿
- [ ] 若改动涉及 AOT 候选模块，运行 `dotnet publish src/<Module> -c Release -p:PublishAot=true` 零警告
- [ ] 公开 API 变更同步更新 `CHANGELOG.md` 的 `[Unreleased]` 节
- [ ] 提交信息遵循 Conventional Commits（`feat(mediator): ...`、`fix(saga): ...`）

---

## 🏗️ CI 层面的贯穿改进（首个 PR 就加上）

这些放在阶段一启动前先做，受益于所有后续阶段：

- [x] **依赖方向检查脚本**：新增 `scripts/check-layering.sh`，验证 `Kun.Core` 的 csproj 不包含任何 `<PackageReference>`（除 `Microsoft.Extensions.*` 抽象包外）
- [x] 把 `check-layering.sh` 接入 `.github/workflows/ci.yml` 作为独立 job
- [x] **公开 API 快照**：为 `Kun.Core`、`Kun.Mediator`、`Kun.Infrastructure`、`Kun.EventBus` 添加 `Microsoft.CodeAnalysis.PublicApiAnalyzers` 包
- [x] 为上述模块创建 `PublicAPI.Shipped.txt` 与 `PublicAPI.Unshipped.txt`
- [x] CI 中加入 `dotnet format --verify-no-changes`（已在 ci.yml，并补了 `dotnet restore` 步骤使其在 format job 中生效）
- [x] 在 `Directory.Build.props` 中添加 `<Version>0.1.0-alpha.1</Version>` 与 `<VersionSuffix>` 处理逻辑
- [ ] 在 GitHub 仓库设置中：保护 `main` 分支，要求 PR 审查 + CI 全绿
- [ ] 新建 `develop` 分支作为开发主干，`main` 仅接受来自 `develop` 的合并

---

## 阶段一：奠基（核心抽象与调度）

**目标**：完善核心抽象，跑通 CQRS 的核心调度链路，提供反射免疫与 AOT 兼容的 Handler 注册机制。

**技能包**：`kun-framework-conventions` + `kun-cqrs-mediator`

**预计耗时**：4–6 天

### 1.1 完善 Kun.Core

- [x] 新增 `src/Kun.Core/Domain/DomainException.cs`：带 `Code` 字段的领域异常基类
- [x] 新增 `src/Kun.Core/Domain/IDomainEventDispatcher.cs`：同进程内领域事件分发接口
- [x] 新增 `src/Kun.Core/MultiTenancy/NullTenantContext.cs`：非多租户场景下的默认实现（`TenantId = null`、`IsMultiTenant = false`）
- [x] 新增 `src/Kun.Core/Security/AnonymousCurrentUser.cs`：未认证场景下的默认实现
- [x] 新增 `src/Kun.Core/Time/ISystemClock.cs`：可注入的时钟抽象（便于测试）
- [x] 新增 `src/Kun.Core/Time/SystemClock.cs`：默认实现使用 `TimeProvider.System`
- [x] 为 `Kun.Core` 所有公开类型补齐 XML 文档注释
- [x] 创建 `tests/Kun.Core.Tests/Domain/AggregateRootTests.cs`：验证领域事件添加/清空行为
- [x] 创建 `tests/Kun.Core.Tests/Domain/EntityTests.cs`：验证按 Id 相等性、瞬时实体不相等
- [x] 创建 `tests/Kun.Core.Tests/Domain/ValueObjectTests.cs`：验证结构相等性、HashCode、运算符
- [x] 创建 `tests/Kun.Core.Tests/Results/ResultTests.cs`：验证 `Result<T>`、`Error` 类型映射、隐式转换
- [x] 运行 `dotnet test tests/Kun.Core.Tests` 全绿（32/32 绿）

### 1.2 实现 Kun.Mediator（AOT 友好调度器）

- [x] 新增 `src/Kun.Mediator/ISender.cs`、`IPublisher.cs` 核心接口（含 `INotification`、`INotificationHandler<T>`）
- [x] 新增 `src/Kun.Mediator/IPipelineBehavior.cs` 与 `RequestHandlerDelegate.cs`
- [x] 新增 `src/Kun.Mediator/RequestHandlerBase.cs`、`RequestHandlerWrapper.cs`（内部类型，承载 Handler 的非泛型调度；另增 `IRequestHandlerWrapper<TResponse>` 实现按响应类型安全转换）
- [x] 新增 `src/Kun.Mediator/KunMediator.cs`：基于 `FrozenDictionary<Type, RequestHandlerBase>` 的 `ISender` + `IPublisher` 实现
- [ ] ⚠️ **延后（ralplan ADR fallback）**：`src/Kun.Mediator.SourceGenerator/` 项目 —— 当前使用手工 `KunMediatorBuilder` 注册替代，AOT 安全零反射；SourceGen 作为 stretch goal 待 M5 后重评
- [ ] ⚠️ 延后：SourceGenerator 扫描 `IRequestHandler<,>` —— 同上
- [ ] ⚠️ 延后：SourceGenerator 生成 `AddKunMediator(this IServiceCollection, Assembly)` —— 已用 `AddKunMediator(Action<KunMediatorBuilder>)` 等价实现
- [ ] ⚠️ 延后：SourceGenerator 为每个 Handler 生成专用 Wrapper —— 当前由 `KunMediatorBuilder.AddRequestHandler<,,>()` 在启动时构造闭包工厂
- [x] 新增 `src/Kun.Mediator/DependencyInjection/KunMediatorServiceCollectionExtensions.cs`：`AddKunMediator` 入口
- [x] 新增 `src/Kun.Mediator/KunMediatorBuilder.cs`：含 `AddRequestHandler<TReq,TResp,THandler>()`、`AddPipelineBehavior<TReq,TResp,TBehavior>()`、`AddNotificationHandler<TNotification,THandler>()`（代替独立 PipelineBehaviorRegistration.cs 文件）
- [x] 新增内置 Behavior：`src/Kun.Mediator/Behaviors/LoggingBehavior.cs`（`[LoggerMessage]` 源生成）
- [x] 新增内置 Behavior：`src/Kun.Mediator/Behaviors/PerformanceBehavior.cs`（500ms 阈值）
- [x] 新增内置 Behavior：`src/Kun.Mediator/Behaviors/TracingBehavior.cs`（`ActivitySource("Kun.Mediator")` Span）
- [x] 创建 `tests/Kun.Mediator.Tests/Dispatch/KunMediatorTests.cs`：Send 路由、未注册请求抛错、CT 传递、Publish 空路径、Publish 多 handler 顺序
- [x] 创建 `tests/Kun.Mediator.Tests/Pipeline/PipelineBehaviorTests.cs`：洋葱顺序、CT 传播、单 behavior 包裹
- [ ] ⚠️ 延后：`tests/Kun.Mediator.Tests/ReflectionFreeTests.cs`（AOT 内存测试）—— 本地 Windows 缺 C++ linker；CI ubuntu-latest 的 AOT publish job 可覆盖
- [ ] ⚠️ 延后：`tests/Kun.Mediator.Tests/SourceGeneratorTests.cs` —— SourceGen 延后，该测试同步延后

### 1.3 实现 Kun.Validation（AOT 友好验证器）

- [x] 新增 `src/Kun.Validation/IValidator.cs`、`ValidationResult.cs`、`ValidationError.cs`、`ValidationException.cs`
- [ ] ⚠️ **延后（同 1.2 ralplan fallback）**：`src/Kun.Validation/Attributes/` 属性包 —— 无 SG 时属性无运行时意义，待 SG 回归时一并 ship
- [ ] ⚠️ 延后：`src/Kun.Validation.SourceGenerator/` 项目
- [ ] ⚠️ 延后：SG 扫描 `[Validate]` / `IValidatable`
- [ ] ⚠️ 延后：SG 生成 `IValidator<T>` 实现
- [ ] ⚠️ 延后：SG 生成 `AddKunValidation` 扩展
- [x] 新增 `src/Kun.Validation/Behaviors/ValidationPipelineBehavior.cs`：Mediator Pipeline Behavior，抛 `ValidationException` 携带错误列表（上层可转 `Result.Fail(Error.Validation(...))`，该转换 behavior 作为 follow-up）
- [x] 新增 `src/Kun.Validation/DependencyInjection/KunValidationServiceCollectionExtensions.cs`：`AddValidator<TRequest, TValidator>()` + `AddValidationBehavior<TRequest, TResponse>()`
- [x] 编写 `tests/Kun.Validation.Tests/ValidatorTests.cs`（测试项目 csproj 已新建 + 注册到 Kun.sln）
- [x] 编写 `tests/Kun.Validation.Tests/ValidationPipelineBehaviorTests.cs`：Mediator 集成，valid 路由到 handler，invalid 抛 ValidationException

### 1.4 阶段一验收

- [x] `dotnet build -c Release` 对 `Kun.Core` / `Kun.Mediator` / `Kun.Validation` 全绿（整个解决方案也 0 错误）
- [x] `dotnet test tests/Kun.Core.Tests tests/Kun.Mediator.Tests tests/Kun.Validation.Tests` 全绿（52/52：32+8+12）
- [ ] ⚠️ 本地延后：`dotnet publish src/Kun.Mediator -c Release -p:PublishAot=true /warnaserror` 零警告 —— 本地 Windows 缺 C++ linker；托 CI ubuntu 的 AOT job（已在 ci.yml 中）
- [ ] ⚠️ 本地延后：`dotnet publish src/Kun.Validation -c Release -p:PublishAot=true /warnaserror` 零警告 —— 同上；Kun.Validation 已加入 ci.yml 的 AOT 发布矩阵
- [x] 生成 `samples/Kun.Sample.Minimal/` —— 控制台 AOT 候选程序，演示 Command/Query/Notification 经 KunMediator 调度 + 三个 Behavior；`dotnet run` 本地验证通过
- [ ] ⚠️ 延后：实测 `Kun.Sample.Minimal` AOT 启动时间 → `docs/benchmarks/aot-startup.md`（同样需要工作的 native linker；可由 CI Linux 环境跑出基线后回填）
- [ ] ⚠️ 延后：发布首个预览版 NuGet `0.1.0-alpha.1`（发布仪式，待用户决定推到哪个 feed）
- [x] 更新 `CHANGELOG.md` 记录阶段一交付

---

## 阶段二：持久化双引擎

**目标**：实现写读分离的持久化，写侧保证聚合根 + Outbox 消息在同一事务中原子落库。

**技能包**：`kun-framework-conventions` + `kun-ddd-modeling` + `kun-outbox-events`

**预计耗时**：5–7 天

### 2.1 实现 Kun.EventBus（抽象层，不含具体传输）

- [ ] 新增 `src/Kun.EventBus/IIntegrationEventOutbox.cs`：面向 Command Handler 的 `Enqueue` 接口
- [ ] 新增 `src/Kun.EventBus/IEventTypeRegistry.cs` + `EventTypeRegistry.cs`：稳定事件 ID ↔ 类型映射
- [ ] 新增 `src/Kun.EventBus/IEventTypeRegistrar.cs`：业务方注册事件类型的抽象
- [ ] 新增 `src/Kun.EventBus/OutboxMessage.cs`：Outbox 消息实体（`Id`、`EventTypeId`、`Payload`、`TraceParent`、`TraceState`、`OccurredAt`、`Status`、`PublishedAt`、`LastError`）
- [ ] 新增 `src/Kun.EventBus/OutboxStatus.cs`：枚举 `Pending`/`Published`/`Failed`
- [ ] 新增 `src/Kun.EventBus/IIntegrationEventHandler.cs` + `MessageContext.cs`
- [ ] 新增 `src/Kun.EventBus/IOutboxPublisher.cs`：后台发布器抽象
- [ ] 新增 `src/Kun.EventBus/KunJsonSerializerOptions.cs`：聚合所有 `JsonSerializerContext`，提供 `GetTypeInfo` 查询
- [ ] 新增 `src/Kun.EventBus/DependencyInjection/KunEventBusBuilder.cs` + `KunEventBusServiceCollectionExtensions.cs`
- [ ] 创建 `tests/Kun.EventBus.Tests/EventTypeRegistryTests.cs`：验证重复注册抛错、未注册查询抛错

### 2.2 实现 Kun.EntityFrameworkCore（写侧）

- [ ] 新增 `src/Kun.EntityFrameworkCore/KunDbContext.cs`：抽象基类，构造器注入 `ITenantContext`
- [ ] `KunDbContext.OnModelCreating`：遍历所有 `IMultiTenant` 实体，自动附加 `TenantId` 全局过滤器
- [ ] `KunDbContext.OnModelCreating`：遍历所有 `ISoftDelete` 实体，自动附加 `IsDeleted == false` 过滤器
- [ ] 新增 `src/Kun.EntityFrameworkCore/Persistence/OutboxMessageConfiguration.cs`：`OutboxMessage` 的 EF 映射（主键、索引 `(Status, OccurredAt)` 支持发布器扫描）
- [ ] 新增 `src/Kun.EntityFrameworkCore/UnitOfWork/EfUnitOfWork.cs`：实现 `IUnitOfWork`
- [ ] `EfUnitOfWork.SaveChangesAsync`：收集聚合根领域事件 → 触发 `IDomainEventDispatcher`（同步） → 把由 Dispatcher 产生的集成事件序列化为 `OutboxMessage` → 与业务变更同事务 `SaveChanges`
- [ ] `EfUnitOfWork`：把 `Activity.Current` 的 `TraceParent`/`TraceState` 写入 `OutboxMessage`
- [ ] `EfUnitOfWork`：序列化走 `KunJsonSerializerOptions.GetTypeInfo(...)`，AOT 安全
- [ ] 新增 `src/Kun.EntityFrameworkCore/Repositories/EfRepository.cs`：`IRepository<T>` 的 EF 实现
- [ ] 新增 `src/Kun.EntityFrameworkCore/Specifications/SpecificationEvaluator.cs`：把 `ISpecification<T>` 应用到 `IQueryable<T>`
- [ ] 新增 `src/Kun.EntityFrameworkCore/DomainEvents/InProcessDomainEventDispatcher.cs`：同步分发领域事件到 `IDomainEventHandler<>`
- [ ] 新增 `src/Kun.EntityFrameworkCore/IntegrationEventOutboxAccumulator.cs`：Scoped 服务，在当前请求内缓冲待落库的集成事件，`SaveChanges` 时被 `EfUnitOfWork` 读取
- [ ] 新增 `src/Kun.EntityFrameworkCore/DependencyInjection/KunEntityFrameworkCoreServiceCollectionExtensions.cs`
- [ ] 新增 `src/Kun.EntityFrameworkCore/UnitOfWorkPipelineBehavior.cs`：Mediator Behavior，Command Handler 成功返回后自动调用 `SaveChangesAsync`
- [ ] Npgsql 支持：通过 `Npgsql.EntityFrameworkCore.PostgreSQL` 包，PostgreSQL 16 成为默认 Provider

### 2.3 实现 Kun.Dapper（读侧）

- [ ] 新增 `src/Kun.Dapper/IReadDbConnectionFactory.cs`：只读连接工厂抽象
- [ ] 新增 `src/Kun.Dapper/NpgsqlReadDbConnectionFactory.cs`：基于 Npgsql 的默认实现
- [ ] 新增 `src/Kun.Dapper/Pagination/PagedResult.cs`：统一分页结果类型
- [ ] 新增 `src/Kun.Dapper/Extensions/DapperAotExtensions.cs`：分页、计数的常用扩展（全部 `[DapperAot]` 标注）
- [ ] 新增 `src/Kun.Dapper/DependencyInjection/KunDapperServiceCollectionExtensions.cs`
- [ ] 撰写 `docs/guides/read-side-patterns.md`：读侧 DTO 规范、租户过滤、标准分页签名、聚合查询模式（配合 `kun-cqrs-mediator` 技能引用）

### 2.4 集成测试（Testcontainers + 真实 PostgreSQL）

- [ ] 在 `tests/Kun.IntegrationTests/` 创建 `Fixtures/PostgresFixture.cs`：基于 Testcontainers.PostgreSql 启动容器、运行 Migrations
- [ ] 在 `tests/Kun.IntegrationTests/` 创建 `Fixtures/TestDbContext.cs`：测试专用 DbContext，含一个示例聚合根
- [ ] 新增 `Respawn` 包（`Directory.Packages.props`），用于测试间快速清库
- [ ] 编写 `tests/Kun.IntegrationTests/Outbox/OutboxAtomicityTests.cs`：**Happy Path** — 聚合根 + 领域事件 → Outbox 消息与业务数据同事务落库
- [ ] **关键测试**：`tests/Kun.IntegrationTests/Outbox/OutboxFailurePathTests.cs` — 聚合根违反 DB 约束时，Outbox 消息**必须**一同回滚（回归阶段二事务漏洞）
- [ ] 编写 `tests/Kun.IntegrationTests/MultiTenancy/TenantFilterTests.cs`：不同租户上下文下查询自动过滤
- [ ] 编写 `tests/Kun.IntegrationTests/SoftDelete/SoftDeleteFilterTests.cs`：软删除实体默认不可见、可通过 `IgnoreQueryFilters` 查到
- [ ] 编写 `tests/Kun.IntegrationTests/Repository/SpecificationEvaluatorTests.cs`：规约模式在 EF 下正确翻译为 SQL
- [ ] 编写 `tests/Kun.IntegrationTests/ReadSide/DapperAotQueryTests.cs`：读侧分页与 DTO 映射

### 2.5 阶段二验收

- [ ] 所有阶段二单元测试与集成测试全绿（`dotnet test`）
- [ ] AOT 发布验证：`Kun.EventBus`、`Kun.Dapper` 零警告（`Kun.EntityFrameworkCore` 只验证 trimming，不强求 AOT）
- [ ] `docs/guides/read-side-patterns.md` 已合入主分支
- [ ] `CHANGELOG.md` 更新
- [ ] 发布 `0.2.0-alpha.1` 预览版

---

## 阶段三：消息总线（RabbitMQ + Outbox 发布器）

**目标**：打通跨服务异步通信链路，保证至少一次投递与消费端幂等。

**技能包**：`kun-framework-conventions` + `kun-outbox-events`

**预计耗时**：3–4 天

### 3.1 实现 Kun.EventBus.RabbitMQ

- [ ] 新增 `src/Kun.EventBus.RabbitMQ/Options/RabbitMqOptions.cs`（`ConnectionString`、`ExchangeName`、`ConsumerName`、`PrefetchCount` 等）
- [ ] 新增 `src/Kun.EventBus.RabbitMQ/RabbitMqConnectionProvider.cs`：连接管理、断线重连
- [ ] 新增 `src/Kun.EventBus.RabbitMQ/Publishing/RabbitMqIntegrationEventPublisher.cs`：把 `OutboxMessage` 转发到 Exchange
- [ ] 发布时把 `TraceParent`/`TraceState` 写入 RabbitMQ Message Headers
- [ ] 新增 `src/Kun.EventBus.RabbitMQ/Consuming/RabbitMqIntegrationEventSubscriber.cs`：订阅队列、反序列化、调度到 Handler
- [ ] 消费时恢复 `Activity.Current`（从 Headers 读取 `TraceParent`）
- [ ] 新增 `src/Kun.EventBus.RabbitMQ/Consuming/IdempotentConsumerDecorator.cs`：基于 `IProcessedMessageStore` 的幂等包装
- [ ] 新增 `src/Kun.EventBus.RabbitMQ/Consuming/DeadLetterHandler.cs`：N 次消费失败进死信队列
- [ ] 新增 `src/Kun.EventBus.RabbitMQ/DependencyInjection/KunRabbitMqBuilder.cs`：`.AddRabbitMQ(...)` 链式 API
- [ ] 新增 `src/Kun.EventBus.RabbitMQ/DependencyInjection/KunRabbitMqBuilder.cs`：`.Subscribe<TEvent, THandler>()` 注册订阅

### 3.2 实现 Outbox 发布后台服务

- [ ] 新增 `src/Kun.EventBus/Background/OutboxPublisherBackgroundService.cs`：`BackgroundService`，扫描 `Pending` 状态消息、批量发布、更新状态
- [ ] 支持批次大小、扫描间隔、重试策略（指数退避）配置
- [ ] 发布失败时记录 `LastError`，超过最大重试次数转 `Failed`
- [ ] 支持单实例锁（基于 `SELECT ... FOR UPDATE SKIP LOCKED` 或 Redis 分布式锁）防止多 Pod 同时扫描同一批消息
- [ ] 发布 `outbox.publish` OpenTelemetry Span，父 Span 来自 `OutboxMessage.TraceParent`

### 3.3 实现基于 Redis 的 IProcessedMessageStore

- [ ] 新增 `src/Kun.EventBus/Idempotency/RedisProcessedMessageStore.cs`：基于 `StackExchange.Redis`，`SETNX + EX` 实现
- [ ] 新增 `src/Kun.EventBus/Idempotency/InMemoryProcessedMessageStore.cs`：测试用内存实现
- [ ] 幂等键格式：`idemp:{consumerName}:{messageId}`，默认 TTL 30 分钟（可配）

### 3.4 集成测试

- [ ] 在 `Kun.IntegrationTests` 中新增 `Fixtures/RabbitMqFixture.cs`（Testcontainers.RabbitMq）
- [ ] 在 `Kun.IntegrationTests` 中新增 `Fixtures/RedisFixture.cs`（Testcontainers.Redis）
- [ ] 编写 `EventBus/PublishConsumeRoundTripTests.cs`：完整生产-消费链路
- [ ] 编写 `EventBus/IdempotencyTests.cs`：同一 `EventId` 重复投递，Handler 只被调用一次
- [ ] 编写 `EventBus/TracePropagationTests.cs`：生产端的 `TraceId` 能在消费端 Handler 中被 `Activity.Current` 读取到
- [ ] 编写 `EventBus/OutboxPublisherResilienceTests.cs`：RabbitMQ 暂时不可用时消息保持 `Pending`，恢复后成功投递
- [ ] 编写 `EventBus/EventVersioningTests.cs`：同时订阅 `v1` 和 `v2`，各走各的 Handler

### 3.5 阶段三验收

- [ ] 所有阶段三集成测试全绿
- [ ] AOT 发布验证：`Kun.EventBus.RabbitMQ` 零警告
- [ ] 手工演练：在一个本地服务发事件，另一个本地服务收到并处理，Aspire Dashboard 里链路完整
- [ ] `CHANGELOG.md` 更新
- [ ] 发布 `0.3.0-alpha.1`

---

## 阶段四：Saga 编排器

**目标**：提供生产可用的分布式事务编排能力，覆盖正向执行、补偿、超时三条关键路径。

**技能包**：`kun-framework-conventions` + `kun-saga` + `kun-outbox-events`

**预计耗时**：5–7 天（当前计划最复杂的阶段）

### 4.1 核心抽象

- [ ] 新增 `src/Kun.Saga/Abstractions/ISagaContext.cs`（`SagaId`、`TenantId`）
- [ ] 新增 `src/Kun.Saga/Abstractions/ISaga.cs` 与 `ISaga<TContext>.cs`：`Name`、`Steps`、`DefaultStepTimeout`
- [ ] 新增 `src/Kun.Saga/Abstractions/ISagaStep.cs`：`Name`、`ExecuteAsync`、`CompensateAsync`、`Timeout`
- [ ] 新增 `src/Kun.Saga/Abstractions/ISagaOrchestrator.cs`
- [ ] 新增 `src/Kun.Saga/Exceptions/SagaStepFailedException.cs`（携带 `StepName`、`Error`）
- [ ] 新增 `src/Kun.Saga/Exceptions/SagaNotFoundException.cs`

### 4.2 状态持久化

- [ ] 新增 `src/Kun.Saga/Persistence/SagaInstance.cs`：`Id`、`SagaName`、`TenantId`、`ContextPayload`、`CurrentStepIndex`、`State`、`StartedAt`、`UpdatedAt`、`TimeoutAt`、`CompletedAt`、`FailureReason`、`TraceId`
- [ ] 新增 `src/Kun.Saga/Persistence/SagaState.cs`：枚举 `Running`/`Compensating`/`Completed`/`Compensated`/`Failed`
- [ ] 新增 `src/Kun.Saga/Persistence/SagaStepQueueEntry.cs`：步骤入队表（`SagaId`、`StepIndex`、`Direction`(Execute/Compensate)、`EnqueuedAt`、`ProcessedAt`）
- [ ] 新增 `src/Kun.Saga/Persistence/ISagaInstanceStore.cs` + EF 实现 `EfSagaInstanceStore.cs`
- [ ] 新增 `SagaInstance` 与 `SagaStepQueueEntry` 的 `IEntityTypeConfiguration` 实现
- [ ] 索引设计：`saga_instances(state, timeout_at)` 支持超时扫描；`saga_step_queue(processed_at, enqueued_at)` 支持执行器拉取

### 4.3 编排器实现

- [ ] 新增 `src/Kun.Saga/Orchestration/SagaOrchestrator.cs`：`StartAsync<TSaga, TContext>`
- [ ] `StartAsync` 在当前 UoW 中持久化 `SagaInstance` 并入队首步（与业务 Command 同事务）
- [ ] 新增 `src/Kun.Saga/Orchestration/SagaStepExecutor.cs`：`BackgroundService`，从 `SagaStepQueueEntry` 拉取未处理记录
- [ ] `SagaStepExecutor`：执行 `ExecuteAsync`，成功则入队下一步或标记完成，失败则切换到 `Compensating` 并反向入队
- [ ] `SagaStepExecutor`：补偿从失败步骤开始，`i-- CompensateAsync`，每步都要幂等（通过 `SagaStepQueueEntry.ProcessedAt`）
- [ ] 新增 `src/Kun.Saga/Orchestration/SagaTimeoutDetector.cs`：`BackgroundService`，每 5 秒扫描 `TimeoutAt < UtcNow AND State = Running` 的实例、触发补偿
- [ ] 所有 Saga 执行/补偿创建 OpenTelemetry Span：`saga.step.execute` / `saga.step.compensate`

### 4.4 依赖注入

- [ ] 新增 `src/Kun.Saga/DependencyInjection/KunSagaBuilder.cs`：`.AddSaga<TSaga, TContext>()` 链式注册
- [ ] SourceGenerator：扫描 `ISagaStep<>` 实现并注册到 DI（Scoped 生命周期）

### 4.5 集成测试（三条关键路径全覆盖）

- [ ] `tests/Kun.Saga.Tests/SagaOrchestratorUnitTests.cs`：纯单元测试，mock Store 与 Step
- [ ] `tests/Kun.IntegrationTests/Saga/SagaHappyPathTests.cs`：**路径 1** — 所有步骤成功 → `State = Completed`
- [ ] `tests/Kun.IntegrationTests/Saga/SagaFailureCompensationTests.cs`：**路径 2** — `step[2]` 失败 → 补偿 `step[1]`、`step[0]` → `State = Compensated`
- [ ] `tests/Kun.IntegrationTests/Saga/SagaTimeoutTests.cs`：**路径 3** — 步骤耗时超过 `Timeout` → 自动补偿
- [ ] `tests/Kun.IntegrationTests/Saga/SagaCompensationIdempotencyTests.cs`：补偿消息重放，下游只执行一次
- [ ] `tests/Kun.IntegrationTests/Saga/SagaCrashRecoveryTests.cs`：模拟进程重启，未完成 Saga 能被 StepExecutor 继续执行

### 4.6 阶段四验收

- [ ] 所有阶段四测试全绿
- [ ] AOT 发布验证：`Kun.Saga` 零警告
- [ ] 可视化验证：通过 SQL 查询 `SELECT * FROM saga_instances` 能清晰看到所有正在运行/已完成/已补偿的 Saga
- [ ] `CHANGELOG.md` 更新
- [ ] 发布 `0.5.0-beta.1`（进入 beta，核心能力齐备）

---

## 阶段五：宿主层、RPC 与安全网关

**目标**：对外暴露 API，实现服务间的高效通信与零信任安全隔离。

**技能包**：`kun-framework-conventions` + `kun-cqrs-mediator`

**预计耗时**：4–5 天

### 5.1 实现 Kun.Security（内部安全体系）

- [ ] 新增 `src/Kun.Security/S2sTokenOptions.cs`：签发者/受众/TTL/签名算法配置
- [ ] 新增 `src/Kun.Security/Jwks/IKunJwksProvider.cs`：公钥集合提供者（网关用）
- [ ] 新增 `src/Kun.Security/Jwks/FileKunJwksProvider.cs`：从文件/Key Vault 加载密钥
- [ ] 新增 `src/Kun.Security/Authentication/KunServiceAuthenticationOptions.cs`
- [ ] 新增 `src/Kun.Security/Authentication/KunServiceAuthenticationExtensions.cs`：`AddKunServiceAuthentication()`，基于 JwtBearer 扩展，自动对接 JWKS 端点
- [ ] 新增 `src/Kun.Security/MultiTenancy/JwtTenantContext.cs`：从当前 `ClaimsPrincipal` 的 `tenant_id` claim 解析
- [ ] 新增 `src/Kun.Security/Security/JwtCurrentUser.cs`：从 `sub`/`roles` claim 解析
- [ ] 新增 `src/Kun.Security/Http/PropagateS2sTokenHandler.cs`：HttpClient 消息处理器，自动把当前请求的 Authorization 头复制到出站
- [ ] 新增 `src/Kun.Security/Extensions/HttpClientBuilderExtensions.cs`：`.AddKunServiceAuth()`
- [ ] 编写 `tests/Kun.Security.Tests/S2sTokenValidationTests.cs`：签名校验、受众校验、过期校验、密钥轮换

### 5.2 实现 Kun.AspNetCore（HTTP 宿主）

- [ ] 新增 `src/Kun.AspNetCore/Endpoints/MinimalApiExtensions.cs`：`MapCommand<TCommand, TResponse>()`、`MapQuery<TQuery, TResponse>()` 快捷方法
- [ ] 新增 `src/Kun.AspNetCore/ProblemDetails/ErrorToProblemDetailsExtensions.cs`：`Error.ToProblemDetails()` 映射
- [ ] 新增 `src/Kun.AspNetCore/ExceptionHandling/KunExceptionHandler.cs`：实现 `IExceptionHandler`，捕获 `ResultFailureException` 与 `DomainException`
- [ ] 新增 `src/Kun.AspNetCore/HealthChecks/KunHealthChecksExtensions.cs`：`AddKunHealthChecks()`，覆盖 DB / RabbitMQ / Redis
- [ ] 新增 `src/Kun.AspNetCore/DependencyInjection/KunAspNetCoreServiceCollectionExtensions.cs`

### 5.3 实现 Kun.Grpc（RPC 宿主）

- [ ] 新增 `src/Kun.Grpc/Interceptors/KunResultToRpcInterceptor.cs`：`Result.Error` → `RpcException` 状态码映射
- [ ] 新增 `src/Kun.Grpc/Interceptors/KunServerAuthInterceptor.cs`：从 gRPC 请求上下文注入 `ITenantContext` 与 `ICurrentUser`
- [ ] 新增 `src/Kun.Grpc/Interceptors/KunTracingInterceptor.cs`：gRPC 服务端/客户端 Activity 增强
- [ ] 新增 `src/Kun.Grpc/DependencyInjection/KunGrpcServiceCollectionExtensions.cs`：`AddKunGrpc()` 一键注册所有拦截器
- [ ] 编写 `tests/Kun.Grpc.Tests/ResultMappingTests.cs`（需创建测试项目）

### 5.4 实现 Kun.Gateway（智能网关）

- [ ] 新增 `src/Kun.Gateway/S2sTokenIssuer.cs`：外部 JWT → 内部 S2S JWT 签发
- [ ] 新增 `src/Kun.Gateway/Transforms/S2sAuthTransform.cs`：YARP RequestTransform，替换 Authorization Header
- [ ] 新增 `src/Kun.Gateway/Configuration/KunGatewayOptions.cs`
- [ ] 新增 `src/Kun.Gateway/Endpoints/JwksEndpoint.cs`：内部 `/.well-known/jwks.json` 端点暴露公钥
- [ ] 新增 `src/Kun.Gateway/RateLimiting/IRateLimitPartitionResolver.cs`：**可插拔**的限流维度抽象
- [ ] 新增 `src/Kun.Gateway/RateLimiting/Resolvers/IpRateLimitPartitionResolver.cs`（默认实现之一）
- [ ] 新增 `src/Kun.Gateway/RateLimiting/Resolvers/TenantRateLimitPartitionResolver.cs`（默认实现之二）
- [ ] 新增 `src/Kun.Gateway/RateLimiting/KunRateLimiterExtensions.cs`：`.AddKunRateLimiter()` 扩展
- [ ] 集成 gRPC-Web 透传（`Grpc.AspNetCore.Web` 包）
- [ ] 新增 `src/Kun.Gateway/DependencyInjection/KunGatewayServiceCollectionExtensions.cs`

### 5.5 契约测试

- [ ] 编写 `tests/Kun.IntegrationTests/Security/S2sTokenRoundTripTests.cs`：网关签发的 Token 能被下游 API 正确校验 + 解析 claim
- [ ] 编写 `tests/Kun.IntegrationTests/Gateway/RateLimitingTests.cs`：租户级与 IP 级限流策略
- [ ] 编写 `tests/Kun.IntegrationTests/Gateway/JwksRotationTests.cs`：多 `kid` 并存，旧令牌仍可校验

### 5.6 阶段五验收

- [ ] 所有阶段五测试全绿
- [ ] AOT 发布验证：`Kun.AspNetCore`、`Kun.Grpc`、`Kun.Gateway`、`Kun.Security` 全部零警告
- [ ] 编写 `docs/security/s2s-jwt.md`：安全架构说明、密钥轮换流程、紧急吊销流程
- [ ] `CHANGELOG.md` 更新
- [ ] 发布 `1.0.0-rc.1`（API 冻结，进入 RC）

---

## 阶段六：示例应用与可观测性落地

**目标**：通过完整示例验证框架端到端可用性；建立可观测性基线。

**技能包**：全部（示例是对整个框架的综合检验）

**预计耗时**：4–5 天

### 6.1 实现 Kun.Sample.Catalog（商品服务）

- [ ] 新增 `samples/Kun.Sample.Catalog/Catalog.Domain/`：`Product`、`Category` 聚合根 + 领域事件
- [ ] 新增 `samples/Kun.Sample.Catalog/Catalog.Application/`：`CreateProductCommand`、`GetProductByIdQuery`、相应 Handler
- [ ] 新增 `samples/Kun.Sample.Catalog/Catalog.Contracts/`：`ProductPublishedIntegrationEvent`、`InventoryChangedIntegrationEvent`
- [ ] 注册稳定事件 ID：`product.published.v1`、`inventory.changed.v1`
- [ ] 新增 `samples/Kun.Sample.Catalog/Catalog.Infrastructure/`：`CatalogDbContext`、`IEntityTypeConfiguration<>`、`ProductReadService`
- [ ] 新增 `samples/Kun.Sample.Catalog/Catalog.Api/`：HTTP + gRPC 双端点，gRPC Proto 文件 `catalog.proto`
- [ ] 创建 EF Core Migrations（初始表 + Outbox 表）

### 6.2 实现 Kun.Sample.Order（订单服务，含 Saga）

- [ ] 新增 `samples/Kun.Sample.Order/Order.Domain/`：`Order` 聚合根
- [ ] 新增 `samples/Kun.Sample.Order/Order.Application/Sagas/OrderCreationSaga.cs`：4 步 Saga（预扣库存 → 支付 → 发货 → 通知）
- [ ] 实现 4 个 Step，每个都有 `ExecuteAsync` + `CompensateAsync`
- [ ] Step 1 `ReserveInventoryStep`：通过 gRPC 调用 Catalog 服务
- [ ] **故意设计的失败点**：`ProcessPaymentStep` 支持通过请求头 `X-Simulate-Failure: payment` 触发失败，用于演示补偿
- [ ] 新增 `samples/Kun.Sample.Order/Order.Api/Endpoints/DemoEndpoints.cs`：
  - [ ] `POST /demo/happy-path`：正常下单
  - [ ] `POST /demo/trigger-payment-failure`：触发补偿路径
  - [ ] `POST /demo/trigger-timeout`：触发超时路径
  - [ ] `GET /demo/sagas/{id}`：查询 Saga 当前状态
- [ ] 创建 EF Core Migrations

### 6.3 .NET Aspire 编排

- [ ] 新增 `samples/Kun.Sample.AppHost/Program.cs`：编排 PostgreSQL、RabbitMQ、Redis、Catalog API、Order API、Gateway
- [ ] 配置 Aspire Dashboard 自动打开
- [ ] `appsettings.json` 按服务拆分，共享配置走 Aspire 的服务发现

### 6.4 OpenTelemetry 链路串联验证

- [ ] 所有服务启用 `OpenTelemetry.Instrumentation.AspNetCore` + `GrpcNetClient` + `Http` + `EntityFrameworkCore`
- [ ] 统一通过 OTLP Exporter 发送到 Aspire Dashboard
- [ ] 验证链路：HTTP 入站 → CQRS Handler → EF SQL → Outbox 发布 → RabbitMQ → 消费端 → 下游 Saga 步骤 全部同一 TraceId
- [ ] 验证 Saga 补偿路径的链路：正向 Trace 与补偿 Trace 通过 `saga_id` 可关联
- [ ] 编写 `docs/observability/tracing-tour.md`：配截图，带读者走一遍链路

### 6.5 示例的集成测试

- [ ] `samples/Kun.Sample.Tests/CatalogApiTests.cs`：通过 `WebApplicationFactory` 测试 HTTP 端点
- [ ] `samples/Kun.Sample.Tests/OrderSagaTests.cs`：完整走通 Happy + Failure + Timeout 三条路径
- [ ] `samples/Kun.Sample.Tests/CrossServiceTracingTests.cs`：断言 TraceId 在跨服务调用中保持一致

### 6.6 文档与发布

- [ ] 撰写 `docs/getting-started.md`：从零到一的 10 分钟上手教程
- [ ] 撰写 `docs/architecture/why-cqrs.md`、`docs/architecture/why-outbox.md`、`docs/architecture/why-saga.md`（每篇 500 字左右）
- [ ] 为 `samples/` 下每个项目写独立 README
- [ ] 更新根 README，把 "快速开始" 章节指向 `Kun.Sample.AppHost` 的实际可运行命令
- [ ] 录制一个 3 分钟的演示视频（可选）：启动 Aspire、触发下单、触发失败、观察补偿

### 6.7 阶段六验收（即 v1.0 发布条件）

- [ ] `dotnet run --project samples/Kun.Sample.AppHost` 能一键启动全部服务并在 Aspire Dashboard 中可视化
- [ ] 三条 Demo 路径（正常 / 失败补偿 / 超时补偿）均可通过 curl 或 Postman 复现
- [ ] Aspire Dashboard 中可看到完整 TraceId 串联
- [ ] `dotnet publish samples/Kun.Sample.AppHost -c Release -p:PublishAot=true` 的所有 AOT 候选子项目零警告
- [ ] `docs/getting-started.md` 被无 Kun 经验的同事测试走通
- [ ] `CHANGELOG.md` 更新 `[1.0.0]` 节
- [ ] 发布 `1.0.0`（正式版）🎉

---

## 📌 里程碑速览

| 里程碑 | 预览版号 | 标志 |
| :--- | :---: | :--- |
| 阶段一完成 | `0.1.0-alpha.1` | CQRS 调度与验证 AOT 跑通 |
| 阶段二完成 | `0.2.0-alpha.1` | 持久化双引擎 + Outbox 原子性验证 |
| 阶段三完成 | `0.3.0-alpha.1` | 消息总线端到端打通 |
| 阶段四完成 | `0.5.0-beta.1` | **核心能力齐备，进入 beta** |
| 阶段五完成 | `1.0.0-rc.1` | **API 冻结，进入 RC** |
| 阶段六完成 | `1.0.0` | **正式版发布 🎉** |

---

## 🎯 给 Claude Code 的执行指令

> 每次开始一项任务时，按以下模式启动对话：
>
> ```
> 我要开始 TODO.md 中的以下任务：
>
> - [ ] 新增 src/Kun.Mediator/ISender.cs...
>
> 请先阅读 .claude/skills/kun-framework-conventions/SKILL.md 和 .claude/skills/kun-cqrs-mediator/SKILL.md，
> 然后完成该任务，并提出一个符合 Conventional Commits 的 commit 信息。
> ```
>
> 每完成一项，Claude 应当：
>
> 1. 更新 `TODO.md`，把该项前的 `- [ ]` 改为 `- [x]`
> 2. 建议 commit 信息
> 3. 提醒是否触发了阶段验收条件
>
> 每完成一个阶段的全部任务后，让 Claude 跑一遍该阶段的"验收"清单，确保所有 checkbox 都已勾选才进入下一阶段。

---

**当前状态**：🏁 前置准备（未开始）
**下一个里程碑**：阶段一完成 → `0.1.0-alpha.1`
