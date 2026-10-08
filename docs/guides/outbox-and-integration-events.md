# Outbox + Integration Events 指南

> 怎么在一个 Kun 服务里把一条领域事件翻译成跨服务的 Integration Event，让它原子地落库、异步地发到 RabbitMQ，并且在消费端幂等。
>
> 参考：`docs/architecture/ADR-015-Outbox-pattern.md` · `docs/architecture/why-outbox.md` · `src/Kun.EventBus/AGENTS.md`

## 适用对象

在 Kun 下游服务里新加一条跨服务消息：

- 需求是"业务写库和事件发送不能分开成功/失败"（典型双写问题）；
- 希望消费者能安全重放（`at-least-once` + 幂等）；
- 希望跨服务追踪（traceparent / tracestate）不断档。

进程内领域事件经 `IDomainEventDispatcher` 在提交前派发；只有通知 handler 显式登记的集成事件进入 Outbox。

## 原子性边界

Kun 的 Outbox 核心是：**聚合根变更 + `OutboxMessage` 行 在同一个 `SaveChangesAsync` 事务里落库**。publish 到 RabbitMQ 是由后台服务 `OutboxPublisherBackgroundService` 异步做的。

```
[Command Handler]
   │
   ├── aggregate.DoSomething()            // 产生 IDomainEvent
   ├── IIntegrationEventBus.Publish(ev)   // 登记到 IntegrationEventBuffer (未落库)
   │
   └── unitOfWork.SaveChangesAsync()
           │
           ├── EfUnitOfWork 收集 ChangeTracker 里的 IDomainEvent
           ├── In-process 分发，domain-event handler 往 Buffer 塞 integration events
           ├── 把 Buffer 里的每一条转成 OutboxMessage 行
           │     • EventTypeId    (从 IEventTypeRegistry 查，例如 "order.placed.v1")
           │     • Payload        (KunJsonContext 源生成序列化，AOT-safe)
           │     • TraceParent / TraceState  (当前 Activity.Current 捕获)
           │     • TenantId       (ITenantContext 注入)
           │     • Status = Pending
           ├── 把 OutboxMessage 行写入同一个 DbContext
           └── DB transaction commit ← 聚合变更 + outbox 行一起成功，或一起失败。
                                          这一步是整个 pattern 的原子边界。
```

如果 `SaveChangesAsync` 之前就直接调 broker（`IIntegrationEventPublisher.PublishAsync`），你就回到了双写问题——broker 成功但 db commit 失败，事件已经出去、库还没改。**别绕过 Outbox**。

## 稳定事件 ID

Integration Event 的身份标识是一个字符串，不是 CLR 类型名：

```
{bounded-context}.{event-name}.v{version}
e.g.  order.placed.v1
      inventory.changed.v2
      payment.refunded.v1
```

用 `AssemblyQualifiedName` 是**错的**——你 rename 一次、搬一个命名空间，消费端就认不出旧消息了。注册通过 `IEventTypeRegistry`（DI 注册时构建），查询通过 `IEventTypeRegistry.Resolve(string) → Type?` / `GetEventType(Type) → string?`。

版本演进：一条事件的 payload shape 变化了，**bump `.v{n}` 后缀、新增一个 CLR 类**，不要原地改 v1 的字段——在途的旧消息必须还能反序列化。

## 发布示例

场景：下单命令触发 `OrderPlaced` 整合事件，下游 `fulfillment` 服务消费它插一行待发货记录。

### 定义 Integration Event

```csharp
public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset PlacedAt);
```

Kun 不强制 marker interface（`IIntegrationEvent` 以约定存在，但发布端只看 `class` 约束 + 注册表里有没有）。把它放在 `Contracts/` 或 `IntegrationEvents/` 子目录，和下游一起维护。

### 注册事件类型

```csharp
services.AddKunEventBus(eb =>
{
    // 无 JsonTypeInfo 的重载 — AOT-unsafe fallback。AOT 服务用下面那行。
    eb.Register<OrderPlacedIntegrationEvent>("order.placed.v1");

    // AOT-safe：传源生成的 JsonTypeInfo
    // eb.Register<OrderPlacedIntegrationEvent>(
    //     "order.placed.v1",
    //     OrderJsonContext.Default.OrderPlacedIntegrationEvent);
});

services.AddKunOutboxPublisher();  // 后台泵，从 Outbox 表拉消息发到 broker
```

`AddKunOutboxPublisher` 负责扫 `OutboxMessage` 表 + 调 `IIntegrationEventPublisher.PublishAsync`（生产环境是 `Kun.EventBus.RabbitMQ`）。参数可选：`BatchSize` / `PollInterval` / `MaxAttempts` / `RetryBaseDelay`。

### 在 Command Handler 里发布

```csharp
public sealed class PlaceOrderCommandHandler
    : IRequestHandler<PlaceOrderCommand, Result<Guid>>
{
    private readonly OrderDbContext _db;
    private readonly IIntegrationEventBus _events;
    private readonly IUnitOfWork _unitOfWork;

    public PlaceOrderCommandHandler(OrderDbContext db, IIntegrationEventBus events, IUnitOfWork unitOfWork)
    {
        _db = db;
        _events = events;
        _unitOfWork = unitOfWork;
    }

    public async ValueTask<Result<Guid>> Handle(PlaceOrderCommand r, CancellationToken ct)
    {
        var order = Order.Place(r.CustomerId, r.Lines);
        _db.Orders.Add(order);

        // Publish 只是"暂存到 IntegrationEventBuffer"，不是 broker 调用。
        // 真正落库 + trace 捕获在 SaveChangesAsync 里。
        _events.Publish(new OrderPlacedIntegrationEvent(order.Id, order.CustomerId, order.PlacedAt));

        await _unitOfWork.SaveChangesAsync(ct);  // ← 原子边界：聚合 + OutboxMessage 一起 commit
        return Result<Guid>.Ok(order.Id);
    }
}
```

如果 `SaveChangesAsync` 抛异常，`Publish` 登记的事件也跟着回滚；`IntegrationEventBuffer` 是 scoped 的，请求结束就释放，不会污染下一个请求。

## 消费示例

下游 `fulfillment` 服务消费 `order.placed.v1`：

```csharp
public sealed class OrderPlacedIntegrationEventHandler
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    private readonly FulfillmentDbContext _db;

    public OrderPlacedIntegrationEventHandler(FulfillmentDbContext db)
    {
        _db = db;
    }

    public ValueTask HandleAsync(
        OrderPlacedIntegrationEvent @event,
        MessageContext context,
        CancellationToken cancellationToken = default)
    {
        // 这里运行时，IdempotentConsumerDecorator 已检查成功记录；handler 成功后才写入标记。
        // 但 at-least-once 是合约底线：Handler 必须对"重复投递"安全。
        // 做法：在 domain 层保证操作幂等（把 context.MessageId 当幂等键），
        //      或在 db 层加一个 unique index 兜底。
        _db.FulfillmentTickets.Add(new FulfillmentTicket(
            IdempotencyKey: context.MessageId,   // ← 用 message id 做业务幂等键
            OrderId: @event.OrderId,
            CreatedAt: @event.PlacedAt));
        return new ValueTask(_db.SaveChangesAsync(cancellationToken));
    }
}
```

注册：

```csharp
services.AddKunEventBus(eb => eb.Register<OrderPlacedIntegrationEvent>("order.placed.v1"));
services.AddRabbitMq(...);  // consumer 侧启动消费端
services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, OrderPlacedIntegrationEventHandler>();
```

`IdempotentConsumerDecorator`（在 `Kun.EventBus.RabbitMQ` 内部，对消费者不可见）会在调 handler 之前对 `{consumerName, messageId}` 做一次 Redis `SET NX` + TTL。重复投递直接跳过 handler。默认 store 是 Redis（`UseInMemoryIdempotency()` 用于本地 / 测试）。

`MessageContext` 属性：
- `MessageId` (Guid) — 一条消息的唯一 id，消费端幂等键首选。
- `EventType` (string) — 稳定事件 id，等同 `OutboxMessage.EventTypeId`。
- `OccurredOn` (DateTimeOffset) — 发布端事件发生时刻。
- `TraceParent` / `TraceState` (string?) — W3C trace header，消费端 ActivitySource 捕获后作为 parent。

## 测试

用 [`Kun.Testing.EntityFrameworkCore`](../../src/Kun.Testing.EntityFrameworkCore/) 里的 `OutboxAssertions`：

```csharp
[Fact]
public async Task PlaceOrder_stages_OrderPlaced_in_outbox()
{
    // Arrange
    var cmd = new PlaceOrderCommand(customerId: Guid.NewGuid(), lines: [...]);

    // Act
    var result = await _mediator.Send(cmd, CancellationToken.None);

    // Assert — 业务路径
    result.ShouldBeSuccess();

    // Assert — Outbox 里正好一条 order.placed.v1 行
    var outboxRow = _dbContext.ShouldHaveExactlyOneOutboxMessage("order.placed.v1");
    outboxRow.Payload.Should().Contain(result.Value.ToString());
}
```

也有 `ShouldHaveOutboxMessage` / `ShouldNotHaveOutboxMessage` 两个同家族 helper。只有 `IEnumerable<OutboxMessage>` 快照？同样三个 helper 都给了 `this IEnumerable<OutboxMessage>` 重载。

## W3C trace 传播

发布端：`OutboxMessage` 写行时把 `Activity.Current?.Id` 存入 `TraceParent`，`Activity.Current?.TraceStateString` 存入 `TraceState`。

`OutboxPublisherBackgroundService` 拉到这一行时，会用 `ActivityContext.TryParse(TraceParent, TraceState, out var ctx)` 还原出 parent context，创建 publish span 作为它的 child。broker 把两个 header 透传到 consumer 侧，consumer 端同样还原后，handler 的所有 span 挂回原请求的 trace 里。

这不同于 ADR-013 的 baggage 富化——baggage 是 **请求作用域**的用户维度（`tenant.id` / `user.id`），跟 trace 上下文是两回事。Outbox 传的是 **trace 上下文本身**。两者互不干扰。

## 反模式

### 1. 直接调 broker 绕过 Outbox

```csharp
// ❌ 不要
await _publisher.PublishAsync(new OrderPlacedIntegrationEvent(...));
await _db.SaveChangesAsync();
```

broker 先成功、db commit 失败的窗口里事件已经发出去了，下游已经处理了一个不存在的订单。走 `IIntegrationEventBus.Publish` + `SaveChangesAsync` 才是原子路径。

### 2. 用 AssemblyQualifiedName 当事件 ID

```csharp
// ❌ 不要
eb.Register<OrderPlacedIntegrationEvent>(typeof(OrderPlacedIntegrationEvent).AssemblyQualifiedName!);
```

命名空间 / 程序集重命名就全盘崩溃，在途消息直接无法反序列化。用稳定字符串 `"order.placed.v1"`。

### 3. 非幂等的消费 Handler

```csharp
// ❌ 不要
public ValueTask HandleAsync(OrderPlacedIntegrationEvent e, MessageContext ctx, CancellationToken ct)
{
    _metrics.IncrementCounter("orders.placed");  // 重复投递 = 计数失真
    _db.Orders.Add(new Order { Id = e.OrderId, ... });  // 重复投递 = 主键冲突
    return _db.SaveChangesAsync(ct);
}
```

`IdempotentConsumerDecorator` 兜底了大多数情况，但 at-least-once 合约要求 handler **自己也安全**。把 `context.MessageId` 当幂等键（see "消费示例"），或在 db 加唯一索引让重复写失败并被 handler catch-swallow。

### 4. 跨 handler 修改 MessageContext

`MessageContext` 是 per-message 元数据，不是共享篮子。别给它加 mutable 字段、别用它传 app-specific state。需要跨服务传数据？塞进 integration event payload 本身。

## 边界 / 后续

ADR-015 还有两个 follow-up 未 ship：

- **§F1 — 死信（dead-letter）**：`MaxAttempts` 次重试后消息进入 `Failed` 状态，目前没有自动 DLQ 表或告警。需要人工捞出来。
- **§F2 — 按租户批次优先级**：目前 `ReadPendingBatchAsync` 按 `OccurredAt` 排序，没有 tenant-level 权重调度。时敏型 webhook SLA 下游有需求时再做。

§F3（`Kun.Testing.EntityFrameworkCore` Outbox 断言）已在 KN-SELF-014 + KN-SELF-016 ship，是上面测试小节直接调的 `OutboxAssertions`。

## 相关

- [ADR-015 Outbox pattern design](../architecture/ADR-015-Outbox-pattern.md) — 完整设计文档，normative。
- [why-outbox.md](../architecture/why-outbox.md) — 入门 primer，解释"为什么不直接双写"。
- [src/Kun.EventBus/AGENTS.md](../../src/Kun.EventBus/AGENTS.md) — abstraction 层约定 + 本指南的一句交叉链接。
- [src/Kun.EventBus.RabbitMQ/AGENTS.md](../../src/Kun.EventBus.RabbitMQ/AGENTS.md) — broker 具体接线。
- [src/Kun.EntityFrameworkCore/AGENTS.md](../../src/Kun.EntityFrameworkCore/AGENTS.md) — 原子边界的具体落库位置。
- [src/Kun.Testing.EntityFrameworkCore/AGENTS.md](../../src/Kun.Testing.EntityFrameworkCore/AGENTS.md) — `OutboxAssertions` 测试 helper。
- [docs/guides/validating-commands.md](./validating-commands.md) — 同家族的 Validation 指南（KN-SELF-013），结构参考。


## 可靠性边界与迁移（S1-KUN-002）

2026-10-09 用真实 Catalog 的 `CreateProductCommandHandler`、领域通知与 PostgreSQL 实证：旧路径商品已写入而 Outbox 为 0。修复后同一断言为 1；通知失败时两者都不提交。

- `KunMediator.Send` 内的 `Publish` 复用当前命令 scope；直接调用 UoW 时默认 `InProcessDomainEventDispatcher` 通过 `IScopedPublisher` 显式复用调用方 scope。自定义领域派发器也必须如此装配，参见 Catalog 示例。通知禁止直接调用 broker 或另起持久化 scope；外部副作用应转换为集成事件。
- `EfUnitOfWork` 提交前 drain 级联领域事件，完成后才把 Buffer 加入业务上下文；数据库保存失败保留暂存消息供同 scope 重试。通知自身失败后该工作单元不可再保存，必须释放 scope 后重试整个命令，防止提交半个通知链。
- `AddKunEntityFrameworkCore()` 默认注册 `EfOutboxMessageStore`，发布器在独立事务中以 `FOR UPDATE SKIP LOCKED` 领取批次并持锁至状态提交，不依赖短 TTL 的全局锁。不同副本领取互斥消息；未提交批次随 scope 释放回滚。
- 自定义非事务存储必须显式配置共享锁（`UseRedisOutboxLock()`）或仅在单实例测试中显式提供 `NullOutboxLockProvider`；默认不再静默无锁运行。Redis 锁的 TTL 方案仍要求宿主保证批次在租期内完成，生产优先框架数据库行锁实现。
- Outbox 新增可空 `NextAttemptAt` 列，首次运行前生成并应用宿主迁移。Catalog 与 Order 已附迁移。发布失败时按 `RetryBaseDelay × 2^(AttemptCount-1)` 退避，封顶一分钟；时间与失败原因一起持久化，重启后仍遵守。达到 `MaxAttempts` 后保留 Failed 行供运维处理。
- RabbitMQ 发布开启 publisher confirms 与 mandatory，未路由到队列的消息不能被误记为 Published；宿主需先部署订阅拓扑。订阅测试显式等待拓扑准备，移除“启动后立即发布”的竞争。
- 消费端先查成功记录，执行 handler，成功标记后才 ack；失败不留成功标记。默认 Redis `idemp:{consumer}:{messageId}` 对应任务所说的 processed_messages 成功记录语义，重试成功后只有一条；已有自定义存储需实现新增 `IsProcessedAsync` 才有预查去重，否则保留至少一次执行。
- 消费失败通过持久化 quorum 重试队列做指数延迟，采用 at-least-once dead-letter 策略与 reject-publish；重试只回到原消费者组的队列，不向其他组重新广播。消息 `Type` 保留稳定事件 ID；达到上限后确认发布到 DLX。仅在重试／死信副本收到 publisher confirm 后 ack 原消息。配置依据 [RabbitMQ 3.13 quorum 队列文档](https://www.rabbitmq.com/docs/3.13/quorum-queues#activating-at-least-once-dead-lettering)。
- 网络断开时保留共享连接的自动恢复实例；发布失败交给持久化 Outbox 重试，不能由发布端销毁订阅器仍持有的恢复连接。

这些机制提供至少一次投递。broker 已确认但 Outbox 状态尚未提交、业务副作用已完成但 Redis 标记尚未写入等崩溃窗口仍会重放，写类 handler 必须通过业务幂等键或同事务消费记录避免重复副作用；不承诺跨数据库、Redis、broker 的 exactly-once。
