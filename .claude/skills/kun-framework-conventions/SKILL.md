---
name: kun-framework-conventions
description: Kun Framework 的核心约定、分层规则、AOT 策略、命名规范、模块依赖关系。当在 Kun 代码库（仓库标识符 `idadawn/kun` 或包含 `Kun.*` 命名空间）中进行任何编码工作时必须使用此技能——包括但不限于添加新模块、新增 NuGet 依赖、修改 csproj、决定某段代码放在哪一层、评审 PR 是否符合框架约定。这是所有其他 Kun-* 技能的共同前置阅读。即使用户只是说"帮我在 Kun 里加一个 XXX"而没有显式提到"约定/规范"，也要先查这个技能来确认分层、命名、AOT 合规。
---

# Kun Framework 约定总纲

本技能是 Kun Framework 所有开发工作的基线规则。涉及领域建模、CQRS、Outbox、Saga、测试等具体任务时，还会有更细的专题技能（`kun-ddd-modeling`、`kun-cqrs-mediator` 等），但**它们都假设你已经遵守了本文件的约定**。

## 一、分层架构（最重要，违反即拒绝合入）

```
┌─────────────────────────────────────────────────────────┐
│ Host Layer：Kun.AspNetCore / Kun.Grpc / Kun.Gateway      │
├─────────────────────────────────────────────────────────┤
│ Infrastructure Layer：Kun.EntityFrameworkCore / Kun.Dapper│
│                       Kun.EventBus.RabbitMQ / Kun.Saga   │
├─────────────────────────────────────────────────────────┤
│ Abstraction Layer：Kun.Mediator / Kun.Infrastructure      │
│                    Kun.EventBus / Kun.Security / Kun.Validation │
├─────────────────────────────────────────────────────────┤
│ Core Layer：Kun.Core（零第三方依赖）                      │
└─────────────────────────────────────────────────────────┘
```

依赖只能由上往下流，不允许反向：

- `Kun.Core` 不引用**任何**第三方包（连 EF Core、Dapper、MediatR 都不行）。它只依赖 BCL。
- `Kun.Mediator`、`Kun.Infrastructure`、`Kun.EventBus`、`Kun.Security`、`Kun.Validation` 只能依赖 `Kun.Core` 和 `Microsoft.Extensions.*` 抽象包。
- `Kun.EntityFrameworkCore`、`Kun.Dapper`、`Kun.EventBus.RabbitMQ`、`Kun.Saga` 是基础设施实现，可以依赖具体 ORM、驱动、消息队列 SDK。
- `Kun.AspNetCore`、`Kun.Grpc`、`Kun.Gateway` 是宿主层，可以依赖所有下层。

## 二、AOT 兼容性策略

每个 csproj 通过 `<IsAotCandidate>true</IsAotCandidate>` 标记是否为 AOT 严格模块。`Directory.Build.props` 会自动对这些模块打开 `IsAotCompatible`、`IsTrimmable`、AOT 分析器。

**AOT 候选模块中的绝对禁令**：

- 禁止 `Activator.CreateInstance`、`Type.GetMethod()`、`MakeGenericType` 等运行时反射 API
- 禁止 `System.Reflection.Emit`（运行时 IL 生成）
- 禁止 `JsonSerializer.Serialize(obj)` 的无参重载——必须显式传 `JsonTypeInfo`，对应类型要注册到 `KunJsonContext`（源生成）
- 禁止 Dapper 的传统 `Query<T>`——改用 `[DapperAot]` 标注方法触发源生成器
- 禁止 `AppDomain.CurrentDomain.GetAssemblies()` 之类的程序集扫描

**AOT 候选模块的状态**：

| 模块 | IsAotCandidate | 说明 |
| :--- | :---: | :--- |
| Kun.Core | ✅ | BCL 唯一依赖 |
| Kun.Mediator | ✅ | `FrozenDictionary` 无反射调度，Handler 通过 SourceGen 注册 |
| Kun.Infrastructure | ✅ | 纯接口 |
| Kun.EntityFrameworkCore | ❌ | EF Core 10 AOT 支持尚不完整，以 trim-safe 为目标 |
| Kun.Dapper | ✅ | Dapper AOT 完整源生成 |
| Kun.EventBus / .RabbitMQ | ✅ | 序列化走 SourceGen JsonContext |
| Kun.Saga | ✅ | Saga 定义通过 DI 注册，步骤通过 SourceGen 绑定 |
| Kun.Security / Validation | ✅ | 签名校验/验证器全部编译期生成 |
| Kun.AspNetCore / Grpc / Gateway | ✅ | 宿主层是 AOT 的主要收益点 |

CI 会对所有 AOT 候选模块执行 `dotnet publish -p:PublishAot=true`，零警告才允许合入。

## 三、命名规范

### 程序集 / 命名空间

- 程序集：`Kun.{Module}`，如 `Kun.Mediator`
- 根命名空间与程序集同名
- 子命名空间反映职责分区：`Kun.Core.Domain`、`Kun.Core.Results`、`Kun.Core.Cqrs`、`Kun.Core.MultiTenancy`

### 类型命名

| 类型 | 约定 | 示例 |
| :--- | :--- | :--- |
| 接口 | `I` 前缀 | `IRepository<T>`、`ITenantContext` |
| 聚合根 | 名词单数 | `Order`、`Product` |
| 值对象 | 名词单数，继承 `ValueObject` | `Money`、`Address` |
| 领域事件 | 过去式 + `Event` 后缀 | `OrderPlacedEvent`、`PaymentRefundedEvent` |
| 集成事件 | 过去式 + `IntegrationEvent` 后缀 | `OrderPlacedIntegrationEvent` |
| Command | 动词 + `Command` 后缀 | `CreateOrderCommand`、`CancelOrderCommand` |
| Query | `Get/List/Search` + `Query` 后缀 | `GetOrderByIdQuery`、`ListActiveOrdersQuery` |
| Handler | `{Request}Handler` | `CreateOrderCommandHandler` |
| Saga | `{业务名词}Saga` | `OrderCreationSaga` |
| Saga 步骤 | 动词短语 + `Step` 后缀 | `ReserveInventoryStep` |

### 稳定事件标识符（关键）

集成事件在 Outbox 中以**稳定字符串 ID** 识别，而非 `AssemblyQualifiedName`：

格式：`{bounded-context}.{event-name}.v{version}`，小写、连字符分隔

示例：

- `order.placed.v1`
- `inventory.reserved.v1`
- `payment.refunded.v2`（第二版 payload 结构）

每个集成事件类型必须在 `IEventTypeRegistry` 中注册对应的稳定 ID，Outbox 序列化/反序列化都通过这个 ID 查找。

## 四、项目结构与 csproj 规范

新建模块时使用此模板：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsAotCandidate>true</IsAotCandidate>   <!-- 或 false -->
    <IsPackable>true</IsPackable>
    <Description>{模块职责的一句话描述}</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kun.Core\Kun.Core.csproj" />
    <!-- 其他依赖（从 Directory.Packages.props 中引用） -->
  </ItemGroup>

</Project>
```

**关键点**：

- `TargetFramework`、`Nullable`、`LangVersion`、`TreatWarningsAsErrors` 已在 `Directory.Build.props` 中统一，**不要在单独的 csproj 里重复**
- 包版本在 `Directory.Packages.props` 集中管理，csproj 里只写 `<PackageReference Include="X" />`，不带 `Version`
- 默认 `<GenerateDocumentationFile>true</GenerateDocumentationFile>`（已在 Build.props 中），公开 API 必须有 XML 文档注释

## 五、Nullable 与错误处理

- `<Nullable>enable</Nullable>` 全仓启用
- 公开 API **不接受** `null` 参数时使用 `ArgumentNullException.ThrowIfNull(arg)`
- 业务失败**永远不抛异常**，返回 `Result<T>` 或 `Result`（`Kun.Core.Results` 命名空间）
- 仅系统性异常（`DbUpdateConcurrencyException`、`RpcException`、网络故障）才上抛
- gRPC 端点可使用 `result.EnsureSuccess()`，由全局拦截器捕获 `ResultFailureException` 并翻译为 `RpcException`

## 六、JSON 序列化（AOT 关键）

所有需要序列化的类型必须注册到 `KunJsonContext`：

```csharp
[JsonSerializable(typeof(OrderPlacedIntegrationEvent))]
[JsonSerializable(typeof(OrderCancelledIntegrationEvent))]
[JsonSerializable(typeof(OutboxMessage))]
internal partial class KunJsonContext : JsonSerializerContext { }
```

调用序列化时显式传 `JsonTypeInfo`：

```csharp
var json = JsonSerializer.Serialize(
    @event,
    KunJsonContext.Default.GetTypeInfo(@event.GetType())!);
```

禁止使用 `JsonSerializer.Serialize(@event)` 的无参重载（会走运行时反射，AOT 下抛异常）。

## 七、提交信息规范

Conventional Commits：`<type>(<scope>): <subject>`

- `type`: `feat` / `fix` / `docs` / `refactor` / `perf` / `test` / `build` / `ci` / `chore`
- `scope`: 模块名去掉 `Kun.` 前缀，如 `mediator`、`saga`、`eventbus`、`security`

示例：`feat(saga): add timeout detection background service`

## 八、做决策时的检查清单

在决定"这段代码放在哪一层"时：

1. 是否涉及 HTTP/gRPC/YARP → 宿主层
2. 是否调用 EF Core、Dapper、RabbitMQ SDK → 基础设施层
3. 是否只使用接口、Result、领域类型 → 抽象层
4. 是否不依赖任何第三方 → Core 层

在决定"这段代码是否 AOT 友好"时：

1. 是否用了反射？→ 用 SourceGen 重写
2. 是否用了 `Type.MakeGenericType`？→ 换成显式泛型调用或注册表查找
3. 是否用了无参 `JsonSerializer.Serialize`？→ 补 `JsonTypeInfo`
4. 是否引入了非 AOT 兼容的包？→ 查找替代方案或把该代码下沉到非 AOT 模块

在添加新 NuGet 依赖前：

1. 先在 `Directory.Packages.props` 查找是否已有
2. 若已有但版本不同，统一到现有版本
3. 若确实要新增，在 `Directory.Packages.props` 先加 `<PackageVersion>`，再在 csproj 里引用
4. AOT 候选模块只能引用 AOT 兼容或 trim-safe 的包

## 九、不要做的事

- ❌ 不要在 `Kun.Core` 的聚合根上添加 `[Column]`、`[Key]`、`[Table]` 等 ORM 属性——这些全部下沉到 `Kun.EntityFrameworkCore` 的 `IEntityTypeConfiguration<T>`
- ❌ 不要在领域事件里直接 `IPublisher.Publish`；事件由聚合根内部 `AddDomainEvent`，UoW 在 `SaveChanges` 时统一收集
- ❌ 不要在 Handler 里直接 `new KunDbContext()`；全部通过 DI 注入仓储
- ❌ 不要在网关的 `S2sTokenIssuer` 中使用对称加密（HS256）；必须用 RS256/ES256 非对称签名，公钥走 JWKS
- ❌ 不要在下游服务信任任何 Header（如 `X-User-Id`），身份必须从有效的 S2S JWT 中提取
- ❌ 不要跨越聚合根边界直接修改子实体的状态——必须通过聚合根的方法
- ❌ 不要在 Outbox 消息里用 `AssemblyQualifiedName` 作为事件类型标识
