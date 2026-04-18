---
name: kun-saga
description: Kun Framework 的 Saga Orchestration 编排器规范。当在 Kun 代码库中定义 ISaga、ISagaStep、编写跨服务的长事务流程（下单→支付→发货、订阅变更、退款链路）、实现步骤的补偿逻辑、处理步骤超时、调试 Saga 卡在某状态时必须使用此技能。包括但不限于：新增 Saga 定义、为步骤实现 ExecuteAsync/CompensateAsync、设计 Saga 上下文（SagaContext）、决定补偿顺序、处理幂等键、编写 Saga 的集成测试。即使用户只说"我要做一个订单流程"或"这个业务涉及调用好几个服务，你帮我串起来"，也要使用本技能以确保遵循 Orchestration 模式、补偿幂等、上下文精简、追踪串联等约束。先读 kun-framework-conventions、kun-cqrs-mediator、kun-outbox-events 再读本技能。
---

# Kun Saga Orchestration 规范

## 一、何时使用 Saga

**使用 Saga 的信号**：

- 一次业务流程需要**跨多个服务**的多个写操作（不能用单库事务覆盖）
- 任何中间步骤失败时需要**回滚已完成步骤**（业务上的补偿）
- 步骤之间有**先后顺序**（库存预留必须在扣款之前，发货必须在扣款之后）

**不要用 Saga 的信号**：

- 单服务内的多聚合变更 → 用 UoW + 事务就够了
- 事件通知型流程（"订单创建后给用户发邮件"）→ 用 `IIntegrationEvent` 发出去就完事，邮件失败不需要回滚订单
- 流程里每一步都是"通知"，没有业务上的"冲正" → 集成事件即可

Saga 的代价：引入了状态表、编排器、补偿逻辑的测试成本。只在真正需要分布式事务语义时才用。

## 二、核心抽象速览

```
ISaga<TContext>        —— Saga 定义（Name + Steps）
ISagaStep<TContext>    —— 单个步骤（Execute + Compensate）
ISagaContext           —— 步骤间共享数据的载体
ISagaOrchestrator      —— 编排器，由框架提供
SagaInstance           —— 状态持久化，存在业务库里
```

## 三、设计 Saga 上下文

**原则**：上下文里**只存 Id 和少量关键数值**，不存聚合根、DTO 列表、大块明细。

```csharp
using Kun.Saga;

namespace Order.Application.Sagas;

public sealed class OrderCreationSagaContext : ISagaContext
{
    public Guid SagaId { get; init; }
    public string TenantId { get; init; } = default!;
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = default!;

    // 各步骤产出的关键 Id —— 补偿需要用这些 Id 来定位要回滚什么
    public Guid? InventoryReservationId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ShipmentId { get; set; }
}
```

**为什么不要塞 `List<OrderLine>`？**

1. `SagaInstance.ContextPayload` 是 JSON 列，塞多了列会非常大，影响扫描性能
2. 订单明细的"真理源"是订单聚合根本身 → 步骤需要时用 `OrderId` 去仓储加载
3. 万一订单在 Saga 运行中被修改（罕见但可能），从 Id 加载能拿到最新状态

## 四、Saga 定义模板

```csharp
namespace Order.Application.Sagas;

public sealed class OrderCreationSaga : ISaga<OrderCreationSagaContext>
{
    public string Name => "order.create";
    public TimeSpan DefaultStepTimeout => TimeSpan.FromSeconds(30);

    public IReadOnlyList<ISagaStep<OrderCreationSagaContext>> Steps { get; }

    public OrderCreationSaga(
        ReserveInventoryStep step1,
        ProcessPaymentStep step2,
        CreateShipmentStep step3,
        NotifyCustomerStep step4)
    {
        // 步骤顺序即执行顺序；补偿反向
        Steps = new ISagaStep<OrderCreationSagaContext>[]
        {
            step1,
            step2,
            step3,
            step4
        };
    }
}
```

**关键点**：

1. `Name` 必须稳定（作为 `SagaInstance.SagaName` 索引）
2. 步骤通过构造器注入（保证 DI 生命周期正确）
3. `DefaultStepTimeout` 是步骤级的默认超时，单步可覆盖

## 五、步骤编写模板

```csharp
namespace Order.Application.Sagas.Steps;

public sealed class ReserveInventoryStep : ISagaStep<OrderCreationSagaContext>
{
    public string Name => "reserve-inventory";
    public TimeSpan? Timeout => TimeSpan.FromSeconds(15);

    private readonly ISender _mediator;
    private readonly ILogger<ReserveInventoryStep> _logger;

    public ReserveInventoryStep(ISender mediator, ILogger<ReserveInventoryStep> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task ExecuteAsync(OrderCreationSagaContext ctx, CancellationToken ct)
    {
        // 通过 Mediator 发出跨服务 Command（可能走 gRPC 客户端）
        var result = await _mediator.Send(
            new ReserveInventoryCommand(
                OrderId: ctx.OrderId,
                TenantId: ctx.TenantId,
                IdempotencyKey: $"{ctx.SagaId}:reserve-inventory"),  // 幂等键
            ct);

        // 失败抛 SagaStepFailedException，编排器会触发补偿
        if (result.IsFailure)
            throw new SagaStepFailedException(Name, result.Error);

        ctx.InventoryReservationId = result.Value;
    }

    public async Task CompensateAsync(OrderCreationSagaContext ctx, CancellationToken ct)
    {
        // 补偿的第一条规则：必须幂等。
        // 如果 Execute 还没来得及写 ReservationId 就失败了，这里必须能安全返回。
        if (ctx.InventoryReservationId is null)
        {
            _logger.LogInformation("ReservationId 为空，跳过库存释放补偿");
            return;
        }

        await _mediator.Send(
            new ReleaseInventoryCommand(
                ReservationId: ctx.InventoryReservationId.Value,
                IdempotencyKey: $"{ctx.SagaId}:release-inventory"),
            ct);
    }
}
```

## 六、补偿的三条铁律

1. **补偿必须幂等**
   - 编排器可能重试补偿
   - 下游可能因网络超时收到多次调用
   - 实现方式：`IdempotencyKey = $"{SagaId}:{StepName}"`，下游记录已处理的 key

2. **补偿必须允许在"部分执行"的上下文上调用**
   - Execute 方法可能执行到一半抛异常（写了本地状态但没写 Context 字段）
   - Compensate 要检查 Context 字段是否有值，没值则跳过而不是抛错
   - 示例：`if (ctx.PaymentId is null) return;`

3. **补偿方向与执行方向相反**
   - Execute 按 `Steps[0] → Steps[N-1]`
   - Compensate 从失败步骤 `i` 开始，按 `i → i-1 → ... → 0`
   - 这是编排器自动做的，步骤实现者不用关心

## 七、启动 Saga

通常在某个 Command Handler 中启动：

```csharp
public sealed class PlaceOrderCommandHandler
    : IRequestHandler<PlaceOrderCommand, Result<Guid>>
{
    private readonly IRepository<Order> _orderRepo;
    private readonly ISagaOrchestrator _sagaOrchestrator;
    private readonly ITenantContext _tenantContext;

    public async ValueTask<Result<Guid>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        // 1) 创建订单聚合（Draft 状态）
        var order = Order.Create(cmd.CustomerId, cmd.Lines);
        await _orderRepo.AddAsync(order, ct);

        // 2) 启动 Saga
        //    —— Saga 的首步入队与订单落库在同一 UoW 事务内
        var sagaCtx = new OrderCreationSagaContext
        {
            SagaId = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId!,
            OrderId = order.Id,
            CustomerId = cmd.CustomerId,
            TotalAmount = order.TotalAmount.Amount,
            Currency = order.TotalAmount.Currency
        };

        await _sagaOrchestrator.StartAsync<OrderCreationSaga, OrderCreationSagaContext>(sagaCtx, ct);

        // UnitOfWorkBehavior 在 Handler 成功返回后统一 SaveChanges
        return order.Id;
    }
}
```

**关键点**：

- `StartAsync` 只把 SagaInstance 和首步消息写入内存变更集
- 实际入库 + 发送首步消息到 Outbox 由 UoW 统一完成
- 这保证了"订单创建成功 ⇔ Saga 一定启动"的原子性

## 八、步骤中调用其他服务

步骤通过 `ISender` 发 Command，而不是直接 `IHttpClientFactory`。这样：

- 内部 Command 通过 Mediator 直接本地处理
- 跨服务 Command 通过 gRPC 客户端（注册为 Mediator 后端）→ 自动带 S2S JWT + 追踪头
- 可以套 `RetryBehavior`、`CircuitBreakerBehavior`

跨服务 Command 的 `IdempotencyKey` 必须包含 `SagaId:StepName`，因为 Outbox 可能重放消息。

## 九、超时

两层超时：

1. **步骤级**：`ISagaStep.Timeout` —— 单步执行的 CancellationToken 超时
2. **Saga 级**：`SagaInstance.TimeoutAt` —— 整个 Saga 的最终时限，由 `SagaTimeoutDetector` 扫描

当 `TimeoutAt < UtcNow` 且 Saga 仍在 `Running` 状态：
- Saga 状态转为 `Compensating`
- `FailureReason` 写 `"Timeout"`
- 从当前步骤开始反向补偿

**典型超时设置**：

| Saga 类型 | 单步超时 | 整体超时 |
| :--- | :--- | :--- |
| 下单流程（需调用支付网关） | 15s | 60s |
| 订阅续费（依赖第三方） | 30s | 5min |
| 批量导入作业 | 60s | 30min |

## 十、Saga 状态查询与运维

`SagaInstance` 表是 Saga 运行时的真理源：

```sql
-- 查当前卡住的 Saga
SELECT saga_name, current_step_index, state, failure_reason, started_at, updated_at
FROM saga_instances
WHERE state IN ('Running', 'Compensating')
  AND updated_at < NOW() - INTERVAL '10 minutes'
ORDER BY started_at DESC;

-- 查某 Saga 的完整上下文
SELECT context_payload, state, current_step_index, failure_reason
FROM saga_instances
WHERE id = '{SagaId}';
```

如果运维需要**手工重试**某个卡住的 Saga，向 `saga_step_queue` 表插入一条"重新入队"记录（框架的 `SagaStepExecutor` 会拾取）。

## 十一、常见反模式

❌ **补偿里抛异常**
→ 编排器要从这一步继续往前补偿；抛异常会让整个 Saga 卡在 `Compensating` 状态。补偿对任何输入都要能安全完成

❌ **Saga Context 塞大对象**
→ JSON 列膨胀、查询慢、序列化开销。只存 Id，明细现查

❌ **步骤 Execute 里使用 `HttpClient` 直接调用服务**
→ 绕过了 Mediator / S2S JWT / 追踪。用 `ISender.Send(Command)`，Command 路由到 gRPC 客户端

❌ **补偿没有幂等键**
→ 重试时下游被执行两次。`IdempotencyKey = "$SagaId:$StepName"`

❌ **步骤顺序依赖上一步的"副作用"而非 Context 字段**
→ 如"第三步假设第二步在 DB 里写了某张表"——Saga 可能因重启丢失执行过程信息。所有跨步骤依赖必须显式通过 Context 传递

❌ **把 Saga 当成通用工作流引擎用**
→ Saga 是分布式事务的一个模式，不是 BPMN。要动态分支、人工审批、跨月运行的流程，应用独立的工作流产品

❌ **在 Saga 步骤里发领域事件给自己的服务处理**
→ 容易死循环。步骤只做"调用下游 Command" 这一种动作

## 十二、测试（简述，详情见 kun-testing）

Saga 测试有三条必测路径：

1. **Happy Path**：所有步骤成功 → `SagaInstance.State = Completed`
2. **中间步骤失败**：`step[2]` 返回 Fail → 补偿 step[1] 和 step[0] → `State = Compensated`
3. **超时路径**：让某步骤耗时超过 `Timeout` → 自动补偿

用 Testcontainers + 真实 PostgreSQL + RabbitMQ，在 `Kun.IntegrationTests` 项目中编写。

## 十三、编写检查清单

设计新 Saga 时：

- [ ] 业务流程真的需要补偿吗？（否 → 用集成事件即可）
- [ ] Context 只含 Id 和关键数值，不含 DTO 列表
- [ ] 每个步骤有稳定 `Name`，`Timeout` 合理
- [ ] 每个步骤的 Compensate 允许 Context 字段为 null
- [ ] 跨服务调用都有 `IdempotencyKey = "$SagaId:$StepName"`
- [ ] Saga 在 Command Handler 中启动，与业务聚合变更同 UoW
- [ ] 集成测试覆盖 Happy / 失败补偿 / 超时三条路径
