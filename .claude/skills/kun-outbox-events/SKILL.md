---
name: kun-outbox-events
description: Kun Framework 的集成事件与 Outbox 模式规范。当在 Kun 代码库中发布跨服务事件（IIntegrationEvent）、注册事件类型到 IEventTypeRegistry、编写消费端 Handler、处理消费幂等、调试 Outbox 消息卡住、升级事件版本（v1 → v2）时必须使用此技能。包括但不限于：把领域事件转换为集成事件、新增 IntegrationEvent 类型、编写 IIntegrationEventHandler、注册稳定事件 ID、为跨服务流程传递 W3C Trace Context。即使用户只说"发个 OrderPlaced 事件给下游"或"加一个事件消费者"，也要使用本技能以确保遵循 Outbox 原子性、稳定事件 ID、消费幂等、追踪传播等约束。先读 kun-framework-conventions 再读本技能。
---

# Kun Outbox 模式与集成事件规范

## 一、核心保证（必须理解）

Kun 的事件驱动遵循四条保证：

1. **原子性**：业务数据变更 + Outbox 消息写入在**同一个 EF Core 事务**中完成（`SaveChanges`），崩溃也不会出现"数据改了但事件丢了"
2. **至少一次投递**：Outbox 发布器保证消息至少送达一次，但可能重复 → **消费端必须幂等**
3. **分布式追踪连续**：`TraceParent`/`TraceState` 随 Outbox 消息落库，发布到 RabbitMQ 时写入 Header，消费端恢复 `Activity.Current`
4. **事件版本演进**：事件类型用**稳定字符串 ID**（如 `order.placed.v1`）标识，不用 `AssemblyQualifiedName`

## 二、领域事件 vs 集成事件

| 维度 | 领域事件 `IDomainEvent` | 集成事件 `IIntegrationEvent` |
| :--- | :--- | :--- |
| 作用域 | 聚合根内部 / 同进程 | 跨服务 / 跨进程 |
| 传递方式 | 同步，UoW 内收集 | 异步，RabbitMQ |
| 命名 | `OrderPlacedEvent` | `OrderPlacedIntegrationEvent` |
| 数据完整性 | 可引用领域对象 | 扁平化、版本化、独立于实现 |
| 消费者 | 本服务内的事件处理器 | 任意订阅方 |

**转换模式**：聚合根产生领域事件 → 本服务的 `IDomainEventHandler` 把它转成 `IIntegrationEvent` 添加到 Outbox → Outbox 发布器推送到 RabbitMQ。

## 三、定义集成事件

```csharp
using Kun.Core.Domain;

namespace Catalog.Contracts.IntegrationEvents;

/// <summary>
/// 商品已发布（v1）。订阅方：搜索服务（建索引）、推荐服务（刷新画像）。
/// 稳定 ID：product.published.v1
/// </summary>
public sealed record ProductPublishedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid ProductId,
    string Name,
    decimal Price,
    string Currency,
    int InitialStock) : IIntegrationEvent;
```

**关键点**：

1. 放在 `Catalog.Contracts` 项目中——这个项目是**下游服务引用的 NuGet 包**的来源，保持纯粹（只有 event record + 无业务逻辑）
2. 字段扁平化，不嵌套领域对象（下游不知道你的领域模型）
3. 金额拆成 `decimal + string currency`，不传 `Money` 值对象（契约要独立于实现）
4. 文档注释里写清**稳定 ID** 与**订阅方**，方便协作

## 四、注册稳定事件 ID

```csharp
// Catalog.Contracts 或 Catalog.Infrastructure 中
internal sealed class CatalogEventTypeRegistrar : IEventTypeRegistrar
{
    public void Register(IEventTypeRegistry registry)
    {
        registry.Register<ProductPublishedIntegrationEvent>("product.published.v1");
        registry.Register<ProductRetiredIntegrationEvent>("product.retired.v1");
        registry.Register<PriceAdjustedIntegrationEvent>("product.priceAdjusted.v2");
    }
}
```

在 `Program.cs` 中启用：

```csharp
builder.Services.AddKunEventBus()
    .AddEventTypeRegistrar<CatalogEventTypeRegistrar>();
```

**注意**：

- 稳定 ID 格式：`{bounded-context}.{event-name}.v{version}`
- 一旦发布绝不修改（破坏历史 Outbox 消息的反序列化）
- 如需不兼容改动，发布 `v2` 并在消费端同时监听 `v1`/`v2` 直到老消息消化完毕

## 五、JSON 序列化（AOT 关键）

所有集成事件必须注册到 `JsonSerializerContext`：

```csharp
namespace Catalog.Contracts;

[JsonSerializable(typeof(ProductPublishedIntegrationEvent))]
[JsonSerializable(typeof(ProductRetiredIntegrationEvent))]
[JsonSerializable(typeof(PriceAdjustedIntegrationEvent))]
internal sealed partial class CatalogJsonContext : JsonSerializerContext
{
}
```

`AddKunEventBus` 会收集所有 `JsonSerializerContext` 注册到内部序列化器。

## 六、从领域事件转换为集成事件

```csharp
using Kun.Core.Domain;
using Kun.EventBus;

namespace Catalog.Application.Products.EventHandlers;

public sealed class ProductPublishedDomainEventHandler
    : IDomainEventHandler<ProductPublishedEvent>
{
    private readonly IIntegrationEventOutbox _outbox;

    public ProductPublishedDomainEventHandler(IIntegrationEventOutbox outbox)
    {
        _outbox = outbox;
    }

    public ValueTask Handle(ProductPublishedEvent @event, CancellationToken ct)
    {
        var integrationEvent = new ProductPublishedIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: @event.OccurredAt,
            ProductId: @event.ProductId,
            Name: @event.Name,
            Price: @event.Price.Amount,
            Currency: @event.Price.Currency,
            InitialStock: @event.InitialStock);

        // 添加到当前 UoW 的 Outbox 缓冲区
        //   —— 不立即写库，SaveChanges 时与业务数据原子落库
        _outbox.Enqueue(integrationEvent);
        return ValueTask.CompletedTask;
    }
}
```

**关键点**：

1. Handler 只 `Enqueue`，不写库（由 EfUnitOfWork 在 `SaveChanges` 时合并到同一事务）
2. `EventId` 用新 Guid（消费端幂等依赖此 ID）
3. 从领域对象提取扁平字段，保持契约稳定

## 七、消费端编写

下游服务（如 Search 服务）消费事件：

```csharp
using Catalog.Contracts.IntegrationEvents;
using Kun.EventBus;

namespace Search.Application.Subscribers;

public sealed class ProductPublishedSubscriber
    : IIntegrationEventHandler<ProductPublishedIntegrationEvent>
{
    private readonly ISearchIndexer _indexer;

    public ProductPublishedSubscriber(ISearchIndexer indexer) => _indexer = indexer;

    public async ValueTask Handle(
        ProductPublishedIntegrationEvent @event,
        MessageContext ctx,
        CancellationToken cancellationToken)
    {
        // Handler 本身不用处理幂等——
        // IdempotentConsumerDecorator 已经通过 IProcessedMessageStore 拦住重复消息
        await _indexer.IndexAsync(new IndexDocument
        {
            Id = @event.ProductId,
            Name = @event.Name,
            Price = @event.Price,
            Currency = @event.Currency
        }, cancellationToken);
    }
}
```

在 `Program.cs` 中订阅：

```csharp
builder.Services.AddKunEventBus()
    .AddRabbitMQ(options =>
    {
        options.ConnectionString = config.GetConnectionString("RabbitMQ")!;
        options.ConsumerName = "search-service";  // 幂等键的 namespace
    })
    .Subscribe<ProductPublishedIntegrationEvent, ProductPublishedSubscriber>();
```

`IdempotentConsumerDecorator` 会在调用你的 Handler 前先通过 `IProcessedMessageStore.TryMarkAsProcessingAsync(event.EventId, "search-service", ...)` 检查；返回 `false` 则跳过，保证同一消息最多被**成功**处理一次。

## 八、事件版本演进

**场景**：`PriceAdjustedIntegrationEvent.v1` 只有 `(ProductId, NewPrice)`，现要加 `OldPrice` 字段以便审计。

**错误做法**：直接改 v1 的 record —— 破坏历史消息。

**正确做法**：

1. 保留 v1 类型不动
2. 新建 v2 类型：
   ```csharp
   public sealed record PriceAdjustedIntegrationEventV2(
       Guid EventId, DateTime OccurredAt,
       Guid ProductId, decimal OldPrice, decimal NewPrice, string Currency) : IIntegrationEvent;
   ```
3. 注册新稳定 ID：`registry.Register<PriceAdjustedIntegrationEventV2>("product.priceAdjusted.v2")`
4. **生产端**：切换到发 v2
5. **消费端**：同时订阅 v1 和 v2；v1 Handler 给 `OldPrice` 填默认值（或从数据库查历史）；跑一段时间后确认 Outbox 中再无 v1 消息，移除 v1 订阅

## 九、Outbox 消息卡住的排查

症状：RabbitMQ 没有收到消息，但业务数据已落库。

排查步骤：

1. **查 Outbox 表**：`SELECT * FROM outbox_messages WHERE status = 'Pending' ORDER BY occurred_at LIMIT 20;`
   - 消息堆积 → `OutboxPublisher` BackgroundService 挂了或没启动
   - 只有少量 → 看具体消息是否序列化异常（查 `last_error`）
2. **查发布器日志**：搜 `outbox.publish` Span，看哪个环节抛异常
3. **查稳定 ID 是否注册**：序列化失败通常是 `IEventTypeRegistry` 里没注册该类型
4. **查 RabbitMQ 连接**：`RabbitMQ.Client` 断线时发布器会重试；若重试耗尽，消息保持 Pending 等待下次循环

## 十、常见反模式

❌ **Handler 里直接 `_rabbitMq.Publish(...)`**
→ 绕过了 Outbox，业务崩溃时事件丢失。永远通过 `IIntegrationEventOutbox.Enqueue`

❌ **在 Command Handler 里直接 `Enqueue` 集成事件**
→ 改在领域事件 Handler 里；Command Handler 只关心聚合根，事件的"副作用"是另一层

❌ **EventId 用业务 Id（如 OrderId）而不是新 Guid**
→ 业务 Id 在重试时相同 → 消费端第一次处理失败、第二次以为是"重复消息"跳过 → 永远处理不了

❌ **Handler 读 HttpContext / 其他 ScopedService 后把结果作为事件字段**
→ 消费者可能是另一个进程/机器；事件必须**自包含**，所有需要的数据都打进 payload

❌ **消费端 Handler 里自己查 Redis/DB 判重**
→ 框架已通过 `IdempotentConsumerDecorator` 处理；自己做会重复劳动且不一致

❌ **稳定 ID 里带版本号却从未真正升级**（如从 `order.placed` 改成 `order.placed.v1` 又改回去）
→ 破坏历史消息；一旦用了 v1，就永远是 v1，演进时发 v2

## 十一、编写检查清单

发布新集成事件时：

- [ ] 定义在 `{Service}.Contracts.IntegrationEvents`，`record` 类型
- [ ] 字段扁平化，不嵌套领域对象
- [ ] 有 `EventId`（新 Guid）和 `OccurredAt`
- [ ] 文档注释写清稳定 ID 与订阅方
- [ ] 注册到 `IEventTypeRegistry`，稳定 ID 格式 `{ctx}.{name}.v{N}`
- [ ] 类型添加到 `JsonSerializerContext`
- [ ] 通过 `IIntegrationEventOutbox.Enqueue` 发布，不直接调 RabbitMQ
- [ ] Enqueue 发生在领域事件 Handler 里（而非 Command Handler 里）

订阅事件时：

- [ ] 实现 `IIntegrationEventHandler<TEvent>`
- [ ] 构造器注入幂等所需的 `ConsumerName`（框架自动从 DI 读取）
- [ ] Handler 保持纯粹 —— 不做幂等判断（框架 Decorator 已处理）
- [ ] `AddKunEventBus().Subscribe<TEvent, THandler>()` 注册
