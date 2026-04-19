# Changelog

本文档记录 Kun Framework 的所有显著变更。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Fixed

- **Kun.Saga**: registered `ISystemClock` in `AddKunSaga` (required by
  `SagaOrchestrator`); `SagaStepExecutor` now persists state via
  `DbContext.SaveChangesAsync` after each entry; forward execution advances
  step index and marks completed; reverse compensation walks back to 0 and
  marks compensated; `ISagaOrchestrator` moved from Singleton to Scoped to
  match its Scoped dependency `ISagaInstanceStore`.

### Added — M1 交付（阶段一：奠基）

**Kun.Core** (v1.1)
- `Domain/DomainException`：带 `Code` 字段的领域异常基类
- `Domain/IDomainEventDispatcher`：同进程内领域事件分发接口
- `MultiTenancy/NullTenantContext`：非多租户默认实现
- `Security/AnonymousCurrentUser`：未认证默认实现
- `Time/ISystemClock` + `Time/SystemClock`：可注入时钟，默认由 `TimeProvider.System` 支持
- 为已有公开类型（AggregateRoot / Entity / ValueObject / Result / Error / Auditing 标记）补齐 XML 注释
- 单元测试 32/32（Domain + Results）

**Kun.Mediator** (v1.2)
- 核心接口：`ISender`、`IPublisher`、`INotification`、`INotificationHandler<T>`、`IPipelineBehavior<TRequest, TResponse>`、`RequestHandlerDelegate<TResponse>`
- `KunMediator`：基于 `FrozenDictionary<Type, RequestHandlerBase>` 的 O(1) 无反射调度
- `KunMediatorBuilder`：手工注册 API —— `AddRequestHandler<TReq, TResp, THandler>()`、`AddPipelineBehavior<TReq, TResp, TBehavior>()`、`AddNotificationHandler<TNotification, THandler>()`
- `DependencyInjection/AddKunMediator` DI 入口
- 内置 Behavior：`LoggingBehavior`、`PerformanceBehavior`（500ms 阈值）、`TracingBehavior`（`ActivitySource("Kun.Mediator")`）
- AOT 安全：所有泛型类型参数带 `[DynamicallyAccessedMembers(PublicConstructors)]`，构建时零 trim 警告
- 单元测试 8/8（Dispatch + Pipeline 洋葱顺序）

**Kun.Validation** (v1.3)
- `IValidator<T>` + `ValidationResult` + `ValidationError` + `ValidationException`
- `Behaviors/ValidationPipelineBehavior<TReq, TResp>`：聚合所有验证器，invalid 时抛 `ValidationException`
- `DependencyInjection/KunValidationServiceCollectionExtensions`：`AddValidator<T, TValidator>()` + `AddValidationBehavior<TReq, TResp>()`
- 单元测试 12/12

**Kun.Sample.Minimal**（新增）
- AOT 候选的控制台样例，演示 Command / Query / Notification 经 `KunMediator` 的完整调度链路
- 附带 `ActivityListener` 观察 `TracingBehavior` 输出
- `dotnet run` 本地验证通过

### Added — 基础设施
- 项目初始化：13 个核心模块骨架
- 白皮书、贡献指南、6 个 Claude 专属 Skills
- `Directory.Build.props` / `Directory.Packages.props` 集中化构建与依赖管理
- CI 工作流：build、test、AOT 发布验证、layering 检查、dotnet format
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` 接入 Kun.Core / Kun.Mediator / Kun.Infrastructure / Kun.EventBus / Kun.Validation
- `scripts/check-layering.sh`：强制 Kun.Core 无第三方 PackageReference
- `CLAUDE.md` 顶层 + 30 个 `AGENTS.md` 分层导航文档
- `Version`：`VersionPrefix=0.1.0`，本地 `VersionSuffix=alpha.1`
- `develop` 分支作为开发主干

### Changed
- `Kun.AspNetCore` / `Kun.Grpc` csproj：从 `Microsoft.NET.Sdk.Web` 修正为 `Microsoft.NET.Sdk`（它们是库，不是 Host）
- `PublicAPI.Unshipped.txt` → `PublicAPI.Shipped.txt`：Kun.Core (137)、Kun.Mediator (32)、Kun.Validation (25) 的 API 冻结入库

### Deferred（M1 已决定延后的项，详见 TODO.md）
- `Kun.Mediator.SourceGenerator` / `Kun.Validation.SourceGenerator`：当前用手工注册 API 替代，SG 作为 stretch goal（ralplan ADR fallback 路径）
- `Kun.Validation` 属性包（`[Required]`、`[MaxLength]` 等）：无 SG 时属性无运行时意义，与 SG 一同回归
- Result-aware 验证 Behavior（返回 `Result.Fail(Error.Validation(...))`）：可在核心抛异常 Behavior 之上封装，follow-up
- AOT 启动时间 benchmark：本地 Windows 缺 C++ linker，延至 CI 或用户本地 Linux 跑

### Added — M2 交付（阶段二：持久化双引擎）

**Kun.EventBus** (v2.1, AOT-candidate abstractions)
- `OutboxMessage`：富领域模型，含 `MarkPublished` / `MarkFailed` / `IncrementAttempt` 状态转换方法
- `OutboxStatus`：`Pending` / `Published` / `Failed` 枚举
- `IntegrationEventBuffer`：scoped 累加器，在 `SaveChangesAsync` 前收集待发事件
- `EventTypeRegistry`：`FrozenDictionary<string, Type>` 支持的 O(1) 稳定事件类型查找；`IEventTypeRegistry` / `IEventTypeRegistrar` 接口
- `IIntegrationEventHandler<TEvent>` + `MessageContext`：消费端处理器契约
- `IIntegrationEventPublisher`：每条消息的 broker 传输抽象
- `IProcessedMessageStore`：幂等消费存储抽象；`InMemoryProcessedMessageStore`（测试用）、`RedisProcessedMessageStore`（SETNX + EX，键格式 `idemp:{consumer}:{messageId}`）
- `IOutboxMessageStore`：持久层扫描抽象
- `OutboxPublisherBackgroundService`：BackgroundService，可配置批量大小、轮询间隔、最大重试次数、指数退避；每条消息启动 `ActivitySource("Kun.EventBus.Outbox")` span，从 `TraceParent`/`TraceState` 列恢复父 trace
- `OutboxPublisherOptions`：BatchSize=50 / PollInterval=2s / MaxAttempts=5 / RetryBaseDelay=1s
- DI 构建器：`KunEventBusBuilder`（`.UseInMemoryIdempotency()` / `.UseRedisIdempotency()`）、`KunEventBusServiceCollectionExtensions`、`KunOutboxPublisherExtensions`
- 单元测试 11/11

**Kun.EntityFrameworkCore** (v2.2, trim-safe, IsAotCandidate=false)
- `KunDbContext`：`IMultiTenant` 全局查询过滤器 + `ISoftDelete` 软删除过滤器
- `OutboxMessageConfiguration`：EF Core `IEntityTypeConfiguration<OutboxMessage>`，含索引
- `EfUnitOfWork`：单次 `SaveChangesAsync` 原子提交聚合变更 + Outbox 行 + 领域事件分发
- `IntegrationEventOutboxAccumulator`：W3C `TraceParent`/`TraceState` 捕获（via `Activity.Current`）
- `UnitOfWorkPipelineBehavior`：Mediator pipeline behavior，写命令专用
- `NoOpDomainEventDispatcher`：默认无操作实现；`MediatorDomainEventDispatcher` 由样例服务提供
- `IUnitOfWork` 接口
- DI：`KunEntityFrameworkCoreServiceCollectionExtensions`
- 单元测试集成（Testcontainers PostgreSQL，Docker 不可用时自动跳过）

**Kun.Dapper** (v2.3, AOT-candidate)
- `IReadDbConnectionFactory`：读端连接工厂抽象；调用方负责 dispose
- `PageRequest`：1-based 页码 + `Offset` 计算属性
- `PagedResult<T>`：`TotalPages` / `HasNextPage` / `HasPreviousPage` + `Empty(PageRequest)` 工厂方法
- `ReadDbOptions`：`ConnectionString` 配置
- DI：`KunDapperServiceCollectionExtensions.AddKunDapper`
- 单元测试 10/10

### Added — M3 交付（阶段三：消息总线）

**Kun.EventBus** (v3.x 追加)
- `IIntegrationEventPublisher`：每条消息的 broker 传输接口（从 M2 正式确定契约）
- `IProcessedMessageStore` 完整实现（InMemory + Redis），含幂等键格式文档

**Kun.EventBus.RabbitMQ** (AOT-candidate，RabbitMQ.Client 7.x)
- `RabbitMqOptions`：ConnectionString / ExchangeName / ConsumerName / PrefetchCount / MaxDeliveryAttempts / IdempotencyTtl
- `RabbitMqConnectionProvider`：单例懒初始化连接，`AutomaticRecoveryEnabled=true`，`IAsyncDisposable`
- `RabbitMqIntegrationEventPublisher`：`IIntegrationEventPublisher` 实现；topic exchange；headers 含 traceparent/tracestate/tenantId/messageId/occurredAt；持久化消息
- `RabbitMqIntegrationEventSubscriber`：BackgroundService；`AsyncEventingBasicConsumer`；per-type durable 队列 + binding；prefetch；W3C trace 恢复；x-death 计数 → DLX
- `IdempotentConsumerDecorator`：handler 成功后调用 `IProcessedMessageStore.TryMarkProcessedAsync`
- `DeadLetterHandler`：`{exchange}.dlx` fanout + `{consumer}.dead-letter` 队列
- `ISubscriptionDispatcher` + `TypedSubscriptionDispatcher<TEvent>`：AOT 安全有类型调度，无 `MakeGenericType`
- `KunRabbitMqBuilder`：`.Subscribe<TEvent, THandler>(eventTypeId, typeInfo)` 流式注册
- `KunRabbitMqServiceCollectionExtensions.AddRabbitMq`
- 单元测试新增 6 个，总计 79/79 通过
- M3 3.4（Testcontainers.RabbitMq 轮转测试）延后至 CI

### Added — M4 交付（阶段四：Saga 编排器）

**Kun.Saga** (AOT-candidate)
- 抽象：`ISagaContext` / `ISaga<TContext>` / `ISagaStep<TContext>` / `ISagaOrchestrator`
- 异常：`SagaStepFailedException` / `SagaNotFoundException`
- 持久化：`SagaInstance`（富状态转换：`StartCompensating` / `Complete` / `Fail`）、`SagaStepQueueEntry`、`SagaState`（Running/Compensating/Completed/Compensated/Failed）、`StepDirection`（Execute/Compensate）
- `ISagaInstanceStore`：持久层抽象；`EfSagaInstanceStore`：EF Core 实现，含 `(state, timeout_at)` 和 `(processed_at, enqueued_at)` 索引
- `SagaOrchestrator`：原子 start + 首步 enqueue（与触发命令同一 EF Core 事务）
- `SagaStepExecutor`：BackgroundService，正向执行 + 逆序补偿；`ActivitySource("Kun.Saga")`，span 名 `saga.step.execute` / `saga.step.compensate`，tags: `saga.step_index` / `saga.saga_id` / `saga.direction` / `saga.name`
- `SagaTimeoutDetector`：BackgroundService，每 5 秒扫描超时实例并触发补偿
- `SagaRegistry`：`FrozenDictionary` + 闭包泛型委托，AOT 安全，无 `MakeGenericType`
- DI：`KunSagaBuilder` + `AddKunSaga` 扩展
- 单元测试 18/18

### Added — M5 交付（阶段五：宿主层、RPC 与安全网关）

**Kun.Security** (M5.1, AOT-candidate)
- `S2sTokenOptions`：Issuer / Audience（默认 `kun-internal`）/ Lifetime（默认 60s）/ SigningAlgorithm（默认 RS256，HS256 明确禁止）/ JwksUri / ValidIssuers
- `IKunJwksProvider` + `FileKunJwksProvider`：文件监视 JWKS 提供者
- `KunServiceAuthenticationExtensions.AddKunServiceAuthentication`：JwtBearer 验证，`ValidAlgorithms=["RS256","ES256"]`，HS256 被拒绝
- `JwtTenantContext`：从 `ClaimsPrincipal` 提取 `tenant_id` 实现 `ITenantContext`
- `JwtCurrentUser`：从 `ClaimsPrincipal` 提取 `sub` / `name` / `roles` 实现 `ICurrentUser`
- `PropagateS2sTokenHandler`：`DelegatingHandler`，将入站 `Authorization` 头复制到出站请求
- DI：`AddKunSecurity`（注册 `ITenantContext` + `ICurrentUser`）/ `HttpClientBuilder.AddKunServiceAuth`
- 单元测试 7/7（valid、HS256 拒绝、wrong aud、expired、key rotation）

**Kun.AspNetCore** (M5.2, AOT-candidate)
- `KunEndpointRouteBuilderExtensions`：`MapCommand<TCommand, TResp>` + `MapQuery<TQuery, TResp>`（ISender glue）
- `ErrorToProblemDetailsExtensions`：`Error.ToProblemDetails()` 映射（Validation→400 / NotFound→404 / Conflict→409 / Unauthorized→401 / Forbidden→403）
- `KunExceptionHandler`：`DomainException` → 400 ProblemDetails
- `KunHealthChecksExtensions`：健康检查注册
- DI：`KunAspNetCoreServiceCollectionExtensions`
- 单元测试 11/11

**Kun.Grpc** (M5.3, AOT-candidate)
- `KunResultToRpcInterceptor`：`ResultFailureException` → `RpcException` 状态码映射；`Error.Code` 复制到 trailers
- `KunServerAuthInterceptor`：强制 `HttpContext.User` 已认证且含 `tenant_id` claim
- `KunTracingInterceptor`：`ActivitySource("Kun.Grpc")` span，`rpc.system=grpc` / `rpc.method` tags；拦截器顺序：Tracing → Auth → ResultMapping
- DI：`AddKunGrpc` 链式注册三个拦截器
- 单元测试 13/13

**Kun.Gateway** (M5.4, AOT-candidate，Web Host)
- `KunGatewayOptions`：ExternalIssuer / ExternalAudience / ExternalJwksUri / InternalIssuer / InternalAudience / InternalTokenLifetime(60s) / SigningKeyPath / PublicKeyPath
- `S2sTokenIssuer`：加载 PEM RSA 私钥，铸造 RS256 JWT，复制 `sub` / `tenant_id` / `roles` 声明
- `S2sAuthTransform`：YARP `RequestTransform`，将 Authorization 头替换为内部 token
- `JwksEndpoint.MapKunJwks`：`/.well-known/jwks.json`，`kid` = SHA-256(modulus) 前 16 字符，AOT 安全（`JwksJsonContext`）
- `IRateLimitPartitionResolver` + `TenantRateLimitPartitionResolver`（claim-based）+ `IpRateLimitPartitionResolver`（X-Forwarded-For aware）
- `KunRateLimiterExtensions`：固定窗口 100 req/min，可插拔分区解析器
- DI：`AddKunGateway` 配置 YARP + S2S issuer + transform
- `Program.cs` 组合根 + `appsettings.json` 骨架
- 单元测试 12/12
- M5 5.5 合约测试（Docker 依赖，延后）

全解决方案：0 错误，0 警告，140/140 测试通过 + 2 集成跳过

### Added — M6 交付（阶段六：示例应用）

**Kun.Sample.Catalog** (M6.1)
- 领域：`Product` AggregateRoot，`Create` / `ChangePrice` / `AdjustStock` 触发 `ProductCreatedDomainEvent` / `PriceChangedDomainEvent` / `StockChangedDomainEvent`
- 契约：`ProductPublishedIntegrationEvent`（稳定 ID `product.published.v1`）/ `InventoryChangedIntegrationEvent`（`inventory.changed.v1`）
- 应用层：`CreateProductCommand` + 验证器 + Handler；`AdjustStockCommand`；`GetProductByIdQuery`（Dapper 读端）；`ProductCreatedHandler`（领域事件 → `IIntegrationEventBus` 桥接）
- 基础设施：`CatalogDbContext`（继承 `KunDbContext`）/ `ProductConfiguration` / `NpgsqlReadDbConnectionFactory` / `MediatorDomainEventDispatcher`
- API：`Program.cs` 组合根；`EnsureCreated` 启动引导；`POST /products` / `GET /products/{id}` / `/health`

**Kun.Sample.Order** (M6.2, Saga 展示)
- 领域：`Order` AggregateRoot，`MarkReserved` / `Paid` / `Shipped` / `Completed` + `Cancel`；`OrderLine` owned value object；`OrderPlaced` / `OrderCancelled` 领域事件
- Saga：`OrderCreationSaga`（4 步，`DefaultStepTimeout=30s`）
  - `ReserveInventoryStep` → `ProcessPaymentStep` → `ShipOrderStep` → `NotifyCustomerStep`
  - 每步含 `ExecuteAsync` + 幂等 `CompensateAsync`
  - `ProcessPaymentStep` 在 `SimulateFailure=="payment"` 时抛异常（演示补偿）
  - `ShipOrderStep` 在 `SimulateFailure=="timeout"` 时 sleep 60s（演示超时）
- 契约：`OrderCompletedIntegrationEvent`（`order.completed.v1`）
- 应用层：`PlaceOrderCommand` / `GetOrderById` / `GetSagaStatus` 查询
- 基础设施：`OrderDbContext` 应用 `ApplyKunSagaConfiguration`；`EfOutboxMessageStore`
- API Endpoints：`POST /demo/happy-path` / `POST /demo/trigger-payment-failure` / `POST /demo/trigger-timeout` / `GET /demo/sagas/{id}`

**Kun.Sample.AppHost** (M6.3, .NET Aspire 9.0)
- 编排：PostgreSQL（+pgAdmin）/ RabbitMQ（+management plugin）/ Redis / Catalog / Order / Gateway
- `WaitFor` 依赖链：各服务等待 postgres + rabbitmq 就绪后启动
- Gateway 引用 catalog + order 用于 YARP 服务发现
- 单命令启动：`dotnet run --project samples/Kun.Sample.AppHost`

**Kun.Sample.Shared** (M6.4, IsAotCandidate=false)
- `OpenTelemetryExtensions.AddKunObservability(serviceName)`：
  - Tracing：ActivitySources `Kun.Mediator` / `Kun.EventBus.Outbox` / `Kun.Grpc` / `Kun.Saga` + AspNetCore + HttpClient + gRPC client
  - Metrics：AspNetCore + HttpClient
  - OTLP exporter（默认指向 Aspire Dashboard collector）
- 框架模块（`src/Kun.*`）保持无 OTel 包依赖；OTel 仅在样例组合根注入
- Gateway 未注入可观测性（AOT-candidate，OTel 包尚未 AOT 安全）
- `OpenTelemetry.Api` 升级至 1.11.x（修复 GHSA-8785-wc3w-h8q6）

### Changed（M2–M6 期间）
- `Directory.Packages.props`：新增 Dapper 2.1.35 / RabbitMQ.Client 7.x / OpenTelemetry 1.11.x / YARP 等包版本
- `Kun.AspNetCore` / `Kun.Grpc` csproj：`IsAotCandidate=true` 确认（库，非 Host）
- `Kun.EntityFrameworkCore` csproj：`IsAotCandidate=false`（trim-safe but not AOT）
- `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`：Kun.Dapper / Kun.EventBus / Kun.EntityFrameworkCore / Kun.Saga / Kun.Security / Kun.Gateway / Kun.Grpc 接入 RS0016 公开 API 追踪

### Deferred（M2–M6 已决定延后的项）
- Source Generators（Mediator / Validation / Saga）：按 ADR fallback 路径继续使用手工注册
- Testcontainers 集成测试（M2 2.4 / M3 3.4 / M5 5.5）：Docker 依赖，CI 环境运行，本地跳过
- `AddKunDapper` API rework：`ReadDbOptions.ConnectionString` 为 `required init`，消费方需在回调内设置（当前 API 略显冗余）
- `IReadDbConnectionFactory` 默认 Npgsql 实现：框架不内置，由消费方提供（参见样例 `NpgsqlReadDbConnectionFactory`）
- `MapQuery` nullable TResponse via JsonTypeInfo：类型系统限制，follow-up
- Gateway 可观测性：OTel 包达到 AOT parity 后追加
- NuGet 发布仪式（手动，TBD）

## [0.1.0] - TBD

首个开发预览版本。

[Unreleased]: https://github.com/idadawn/kun/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/idadawn/kun/releases/tag/v0.1.0
