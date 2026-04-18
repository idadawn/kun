# Changelog

本文档记录 Kun Framework 的所有显著变更。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

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

## [0.1.0] - TBD

首个开发预览版本。

[Unreleased]: https://github.com/idadawn/kun/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/idadawn/kun/releases/tag/v0.1.0
