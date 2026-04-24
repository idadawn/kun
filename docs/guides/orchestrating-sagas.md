# Saga 编排指南（Orchestration）

> 怎么在 Kun 里定义一个中心协调的 Saga：按顺序执行多个可补偿步骤，任一步骤失败时反向运行 `CompensateAsync`，进程重启后继续，超时自动回滚。
>
> 参考：`docs/architecture/why-saga.md` · `src/Kun.Saga/AGENTS.md`

## 适用对象

在 `neuroagent-csharp` 或其他 Kun 下游服务里跑一个跨多个聚合 / 多个服务的业务流程：

- 多步业务，部分步骤成功后出错必须回滚前置影响（典型：下单 → 扣库存 → 扣款 → 发货，三步成功后发货失败要把前两步反向撤销）；
- 需要在进程重启 / crash 后恢复并继续推进；
- 需要中心可见性——一次流程的状态机在一个地方看得全（而不是分散在各消费者的事件订阅树里）；
- 每个步骤的作用是 **本地**（单个服务 / 单个聚合 DB）事务，但整个流程 **跨** 作用域。

如果你的流程能塞进一个 DB transaction，用 `IUnitOfWork` / Command handler + Outbox 就好，别上 Saga。Saga 的代价是中心 coordinator + 持久化状态机 + 补偿逻辑双写。

## Orchestration 而不是 Choreography

Kun 的 Saga 是 **中心编排** 模型。每一步由中心 `SagaOrchestrator` 主动驱动；步骤不知道下一个是谁，也不发 event 让别人"订阅下一步"。Choreography（每步发 event、大家各自订阅）在框架里明确不支持——它把流程散到 N 个消费者里，状态不可见、补偿顺序混乱、链路超时无法统一检测。详见 [`docs/architecture/why-saga.md`](../architecture/why-saga.md)。

## `ISaga` + `ISagaStep` 接口

工程师只实现两个接口（都在 `Kun.Saga.Abstractions`）：

```csharp
public interface ISaga<TContext> where TContext : ISagaContext
{
    string Name { get; }                                  // "OrderCreationSaga" — 持久化用的稳定 ID
    TimeSpan DefaultStepTimeout { get; }                  // 默认 step 超时；步骤可以覆盖
    IReadOnlyList<ISagaStep<TContext>> Steps { get; }     // 正向执行顺序；补偿反序
}

public interface ISagaStep<TContext> where TContext : ISagaContext
{
    string Name { get; }                                  // "reserve-inventory" — 稳定 step ID
    TimeSpan? Timeout { get; }                            // null → 用 saga 的 DefaultStepTimeout
    ValueTask ExecuteAsync(TContext context, CancellationToken ct);
    ValueTask CompensateAsync(TContext context, CancellationToken ct);
}
```

`TContext` 是你自己定义的 record / class，实现空接口 `ISagaContext`。它承载跨步骤的状态（订单 id、已扣款金额、reservation token 之类）。

**关键规矩**：
- `ISaga.Name` / `ISagaStep.Name` 是 **字符串**，持久化到 `saga_instances.saga_name` / 步骤记录里。**不要** 用 `typeof(X).Name`——refactor 改名后旧 saga 实例就断了。
- 同一个 Saga 的 `Steps` 顺序是幂等的、不可变的：一旦某个版本的流程启动实例，代码里对 Steps 的任何增删改都必须通过新 Saga 类型（或至少明确版本管理）完成。
- `CompensateAsync` **必须** 对"步骤从未成功执行"也是安全的——补偿链从失败点往前反向跑，可能会碰到一些 step 本来就没落库过。

## 原子启动边界

启动一个 Saga 和触发它的业务命令 **必须** 在同一个 `DbContext` transaction 内：

```
[Command Handler]
   │
   ├── aggregate.DoSomething()                // 产生 IDomainEvent
   ├── await _orchestrator.StartAsync<OrderCreationSaga, OrderCreationContext>(
   │         new OrderCreationContext(order.Id, ...), ct);
   │       │
   │       ├── 插入 SagaInstance 行
   │       │     (Id, SagaName, ContextJson, State=Pending, CreatedAt, TimeoutAt)
   │       ├── 把 "执行第一步" 的 OutboxMessage 行塞进 outbox
   │       └── 返回新分配的 SagaInstance.Id (Guid)
   │
   └── await _db.SaveChangesAsync(ct)         // ← 原子边界：
                                               //   聚合变更 + SagaInstance 行 + 第一步 Outbox 行
                                               //   一起 commit 或一起回滚
```

后台 `SagaStepExecutor` 拉 Outbox 里的 step-message、加载 `SagaInstance`、调对应 step 的 `ExecuteAsync`。成功则登记下一步的 Outbox 消息，失败（或超时）则切 `SagaInstance.State = Compensating` 并反向登记补偿消息。执行器本身 at-least-once，意味着 step 的 `ExecuteAsync` / `CompensateAsync` 可能被重放——所以二者都必须幂等（详见下文）。

## Context + `JsonTypeInfo`（AOT-safe）

`TContext` 要序列化进 `SagaInstance.ContextJson` 以支持跨进程恢复。序列化走源生成的 `JsonSerializerContext`，注册时 **必须** 传对应的 `JsonTypeInfo`：

```csharp
[JsonSerializable(typeof(OrderCreationContext))]
internal partial class OrderJsonContext : JsonSerializerContext;
```

```csharp
services.AddKunSaga(b =>
    b.AddSaga<OrderCreationSaga, OrderCreationContext>(
        OrderJsonContext.Default.OrderCreationContext));
```

没有这条源生成绑定，序列化会落到反射路径，AOT 发布会警告。

## 完整例子：3 步 `OrderCreationSaga`

场景：下单流程——扣库存 → 扣款 → 标记为已发货。任一步失败反向补偿。

### Context

```csharp
public sealed record OrderCreationContext(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string? InventoryReservationId = null,    // 由 ReserveInventoryStep 填入
    string? PaymentTransactionId = null)      // 由 ProcessPaymentStep 填入
    : ISagaContext;
```

### Saga

```csharp
public sealed class OrderCreationSaga : ISaga<OrderCreationContext>
{
    public string Name => "OrderCreationSaga";

    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(30);

    public IReadOnlyList<ISagaStep<OrderCreationContext>> Steps { get; }

    public OrderCreationSaga(
        ReserveInventoryStep reserve,
        ProcessPaymentStep pay,
        MarkShippedStep ship)
    {
        Steps = [reserve, pay, ship];
    }
}
```

### Step 1 — `ReserveInventoryStep`

```csharp
public sealed class ReserveInventoryStep : ISagaStep<OrderCreationContext>
{
    private readonly IInventoryService _inventory;

    public ReserveInventoryStep(IInventoryService inventory) => _inventory = inventory;

    public string Name => "reserve-inventory";
    public TimeSpan? Timeout => null;  // 用 saga 默认

    public async ValueTask ExecuteAsync(OrderCreationContext ctx, CancellationToken ct)
    {
        // 幂等：如果上下文里已经有 reservationId，说明这一步之前跑成功过，直接返回。
        if (ctx.InventoryReservationId is not null) return;

        var reservationId = await _inventory.ReserveAsync(ctx.OrderId, ct);
        ctx with { InventoryReservationId = reservationId };  // 更新 context
    }

    public async ValueTask CompensateAsync(OrderCreationContext ctx, CancellationToken ct)
    {
        // "什么都没做" 是合法补偿路径：ReservationId 为 null 就直接返回。
        if (ctx.InventoryReservationId is null) return;

        await _inventory.ReleaseAsync(ctx.InventoryReservationId, ct);
    }
}
```

### Step 2 — `ProcessPaymentStep`

```csharp
public sealed class ProcessPaymentStep : ISagaStep<OrderCreationContext>
{
    private readonly IPaymentService _pay;

    public ProcessPaymentStep(IPaymentService pay) => _pay = pay;

    public string Name => "process-payment";
    public TimeSpan? Timeout => TimeSpan.FromSeconds(10);  // 支付网关 SLA 更严

    public async ValueTask ExecuteAsync(OrderCreationContext ctx, CancellationToken ct)
    {
        if (ctx.PaymentTransactionId is not null) return;
        var txId = await _pay.ChargeAsync(ctx.CustomerId, ctx.Amount, ct);
        ctx with { PaymentTransactionId = txId };
    }

    public async ValueTask CompensateAsync(OrderCreationContext ctx, CancellationToken ct)
    {
        if (ctx.PaymentTransactionId is null) return;
        await _pay.RefundAsync(ctx.PaymentTransactionId, ct);
    }
}
```

### Step 3 — `MarkShippedStep`

```csharp
public sealed class MarkShippedStep : ISagaStep<OrderCreationContext>
{
    private readonly OrderDbContext _db;

    public MarkShippedStep(OrderDbContext db) => _db = db;

    public string Name => "mark-shipped";
    public TimeSpan? Timeout => null;

    public async ValueTask ExecuteAsync(OrderCreationContext ctx, CancellationToken ct)
    {
        var order = await _db.Orders.FindAsync([ctx.OrderId], ct);
        if (order is null || order.IsShipped) return;  // 幂等兜底
        order.MarkShipped();
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask CompensateAsync(OrderCreationContext ctx, CancellationToken ct)
    {
        var order = await _db.Orders.FindAsync([ctx.OrderId], ct);
        if (order is null || !order.IsShipped) return;
        order.UnmarkShipped();
        await _db.SaveChangesAsync(ct);
    }
}
```

### 启动 Saga

```csharp
public sealed class PlaceOrderCommandHandler
    : IRequestHandler<PlaceOrderCommand, Result<Guid>>
{
    private readonly OrderDbContext _db;
    private readonly ISagaOrchestrator _sagas;

    public PlaceOrderCommandHandler(OrderDbContext db, ISagaOrchestrator sagas)
    {
        _db = db;
        _sagas = sagas;
    }

    public async ValueTask<Result<Guid>> Handle(PlaceOrderCommand r, CancellationToken ct)
    {
        var order = Order.Create(r.CustomerId, r.Amount);
        _db.Orders.Add(order);

        var sagaId = await _sagas.StartAsync<OrderCreationSaga, OrderCreationContext>(
            new OrderCreationContext(order.Id, r.CustomerId, r.Amount),
            ct);

        await _db.SaveChangesAsync(ct);  // ← 原子边界：order + SagaInstance + 第一步 outbox
        return Result<Guid>.Ok(order.Id);
    }
}
```

### 注册

```csharp
services.AddKunSaga(b =>
    b.AddSaga<OrderCreationSaga, OrderCreationContext>(
        OrderJsonContext.Default.OrderCreationContext));

services.AddScoped<ReserveInventoryStep>();
services.AddScoped<ProcessPaymentStep>();
services.AddScoped<MarkShippedStep>();
services.AddScoped<ISaga<OrderCreationContext>, OrderCreationSaga>();
```

## 补偿 + 超时 生命周期

`SagaInstance.State` 的状态机：

```
  StartAsync                    step.ExecuteAsync 成功
  ─────────▶ Pending ──────────────────────▶ Pending (n+1)
                 │                               │
                 │ step 失败 / 超时              │ 最后一步成功
                 ▼                               ▼
             Compensating ──────────────▶ Completed
                 │
                 │ 所有已执行的 step 反向 CompensateAsync 跑完
                 ▼
             Compensated
```

- **Step 失败**：`SagaStepExecutor` 捕获 `ExecuteAsync` 抛异常，把 `SagaInstance.State` 切到 `Compensating`，反向登记补偿消息。
- **Step 超时**：`SagaTimeoutDetector` 后台扫 `SagaInstance.TimeoutAt < UtcNow` 的行，同样切到 `Compensating`。
- **补偿完成**：所有已执行的 step `CompensateAsync` 跑完，`State = Compensated`，流程终止。
- **成功完成**：最后一步 `ExecuteAsync` 返回，`State = Completed`。

## 幂等（最关键的约束）

**`ExecuteAsync` 和 `CompensateAsync` 都必须幂等。** 原因：`SagaStepExecutor` 是 at-least-once 的（基于 Outbox + `IProcessedMessageStore`），同一个 step message 可能被投递两次。

幂等实现路径：
- **业务层天然幂等**：`order.MarkShipped()` 是设 flag，重复设还是 true——天然安全。
- **Context 检查**：上文 `ReserveInventoryStep` 用 `ctx.InventoryReservationId is not null` 早退。
- **外部服务幂等键**：调第三方时把 `sagaId + stepName` 作为幂等键传过去，让对方去重。

**`CompensateAsync` 的"空补偿"是正常路径，不要抛异常**：补偿链从失败点往前反向跑，前置 step 可能根本没 `Execute` 成功过（例如 step 1 就失败了，根本没到 step 2）；`CompensateAsync` 必须对这种情况 no-op 返回。

## 反模式

### 1. 跨 step 共享可变单例状态

```csharp
// ❌ 不要
public sealed class ReserveInventoryStep
{
    public static string? LastReservationId;  // ← 静态变量承载 step 间状态

    public ValueTask ExecuteAsync(OrderCreationContext ctx, CancellationToken ct)
    {
        LastReservationId = /* ... */;
        return default;
    }
}
```

进程重启就丢了；并发跑多个 saga 时相互覆盖；根本无法恢复。状态只能进 `TContext`——那是唯一会被 Outbox 持久化 + 跨进程恢复的载体。

### 2. `CompensateAsync` 抛异常兜不住 "nothing to undo"

```csharp
// ❌ 不要
public async ValueTask CompensateAsync(OrderCreationContext ctx, CancellationToken ct)
{
    // 不检查 ctx.PaymentTransactionId 是否为 null——直接调 refund。
    await _pay.RefundAsync(ctx.PaymentTransactionId!, ct);  // 丢到空引用抛异常
}
```

异常冒上来 → `SagaStepExecutor` 捕获 → 补偿消息被标记失败 → saga 停在 `Compensating` 不动，需要人工救场。始终 guard："`if (ctx.PaymentTransactionId is null) return;`"。

### 3. 用 CLR 类名当 step / saga ID

```csharp
// ❌ 不要
public string Name => nameof(ReserveInventoryStep);  // → "ReserveInventoryStep"
```

有人 refactor 把 `ReserveInventoryStep` rename 成 `ReserveStockStep`，数据库里存的 `saga_steps.step_name = "ReserveInventoryStep"` 的行瞬间对不上代码里新加载的任何 step 了——in-flight saga 全卡住。用 kebab-case 稳定字符串：`"reserve-inventory"`。

### 4. 让 step 发 integration event 让其他 step 订阅（Choreography 渗透）

```csharp
// ❌ 不要
public async ValueTask ExecuteAsync(OrderCreationContext ctx, CancellationToken ct)
{
    await _inventory.ReserveAsync(ctx.OrderId, ct);
    _events.Publish(new InventoryReservedEvent(ctx.OrderId));  // ← 诱导下一个 step 的 handler 订阅
}
```

一旦 step 之间靠 event 触发，中心 coordinator 的可见性就废了——`SagaInstance.State` 和真实进度脱节、补偿顺序不确定、超时检测失效。`Steps` 顺序里下一个 step 怎么启动？`SagaStepExecutor` 在上一个成功后自己把下一个 step message 推进 Outbox，**不需要** step 自己发 event。

## 边界 / 后续

- **`P2-KN-Saga-EF-Split`**：目前 `Kun.Saga` 因为传递依赖 EF Core 被标 `IsAotCandidate=false`。未来会拆出 `Kun.Saga.EntityFrameworkCore`（和 `Kun.Testing.EntityFrameworkCore` 一样的模式）把 abstraction 层恢复 AOT。见 `docs/architecture/AOT-audit-2026-04-23.md` 跟踪项。
- **测试 helper 未 ship**：Saga 侧目前没有对应的 `Kun.Testing.Saga`（类似 KN-SELF-016 给 Outbox 准备的 `OutboxAssertions`）。消费者今天要测 saga 的话手写断言：`dbContext.Set<SagaInstance>().Single(i => i.Id == sagaId).State.Should().Be(SagaState.Completed)`。有具体消费者痛点时再独立跟一个 KN-SELF。
- **Saga 版本演进**：目前没有显式的版本字段。实际做法是 **新建一个 saga 类型**（`OrderCreationSagaV2`）而不是原地改 `OrderCreationSaga.Steps`；在途旧 saga 会按旧代码走完。若出现需要在线 migrate 的需求再做版本化。

## 相关

- [docs/architecture/why-saga.md](../architecture/why-saga.md) — 入门 primer，解释"为什么是 Orchestration 不是 Choreography"、"为什么不是 2PC"。
- [src/Kun.Saga/AGENTS.md](../../src/Kun.Saga/AGENTS.md) — 模块内约定 + 本指南的一句交叉链接。
- [docs/guides/outbox-and-integration-events.md](./outbox-and-integration-events.md) — Saga 的 step 执行器靠 Outbox 驱动；理解 Saga 前先把 Outbox 吃透。
- [docs/guides/validating-commands.md](./validating-commands.md) — 同家族的 Validation 指南，结构参考。
- [docs/architecture/AOT-audit-2026-04-23.md](../architecture/AOT-audit-2026-04-23.md) — `P2-KN-Saga-EF-Split` 跟踪。
