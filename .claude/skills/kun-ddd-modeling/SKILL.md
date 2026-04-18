---
name: kun-ddd-modeling
description: Kun Framework 的 DDD 领域建模规范。当在 Kun 代码库中创建或修改聚合根（AggregateRoot）、实体（Entity）、值对象（ValueObject）、领域事件（IDomainEvent）、仓储（IRepository）、规约（Specification）时必须使用此技能。包括但不限于：新增业务聚合根（如 Order、Product）、为聚合根添加业务方法、拆分聚合边界、定义值对象（如 Money、Address）、为业务变更触发领域事件、编写自定义 Specification。即使用户只说"帮我写个 Order 类"或"加一个 User 实体"，也要使用本技能以确保遵循 Kun 的 DDD 分层与纯净领域层原则。先读 kun-framework-conventions 再读本技能。
---

# Kun DDD 领域建模规范

## 一、三大构建块的区分（最基础、最关键）

| 构建块 | 定位 | 相等性 | 可变性 | 示例 |
| :--- | :--- | :--- | :--- | :--- |
| **聚合根** | 一致性边界、事务边界 | 按 Id | 可变（仅通过方法） | `Order`、`Product` |
| **实体** | 聚合内的子对象 | 按 Id | 可变（通过聚合根） | `OrderLine` |
| **值对象** | 描述某个属性/概念 | 按所有组成字段 | 不可变 | `Money`、`Address` |

**判断准则**：

- "两个实例 Id 相同即相等" → 实体（含聚合根）
- "两个实例字段全等即相等" → 值对象
- "持有其他实体的生命周期、是外部交互入口" → 聚合根
- "生命周期依附于某个聚合根" → 实体（不是聚合根）

## 二、领域层的纯净性（硬性要求）

**领域层（`Kun.Core.Domain` 及业务项目的 Domain 目录）零基础设施依赖**。以下在领域层中永远禁止：

- EF Core 属性：`[Key]`、`[Column]`、`[Table]`、`[ForeignKey]`、`[Index]`、`[NotMapped]`
- JSON 属性：`[JsonPropertyName]`、`[JsonIgnore]`、`[JsonConstructor]`
- 验证属性：`[Required]`、`[MaxLength]`、`[Range]`（验证由 `Kun.Validation` SourceGen 完成）
- 任何 `using Microsoft.EntityFrameworkCore;`、`using Dapper;`、`using Newtonsoft.Json;`

持久化映射下沉到 `IEntityTypeConfiguration<T>`，位于 `Kun.EntityFrameworkCore` 或业务项目的 Infrastructure 目录。

## 三、聚合根编写模板

```csharp
using Kun.Core.Domain;

namespace MyService.Domain.Orders;

public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderLine> _lines = new();

    // 属性 private set，只能通过方法修改
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public Money TotalAmount { get; private set; } = default!;
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    // EF Core 需要无参构造器；标 private 避免业务误用
    private Order() { }

    // 工厂方法，封装创建逻辑 + 不变量校验 + 领域事件
    public static Order Create(Guid customerId, IEnumerable<OrderLineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var lineList = lines.ToList();
        if (lineList.Count == 0)
            throw new DomainException("order.create.empty", "订单必须包含至少一行。");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = OrderStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var input in lineList)
            order.AddLine(input.ProductId, input.Quantity, input.UnitPrice);

        order.RecalculateTotal();
        order.AddDomainEvent(new OrderPlacedEvent(order.Id, order.CustomerId, order.TotalAmount));
        return order;
    }

    // 业务方法：保护不变量 + 触发领域事件
    public void AddLine(Guid productId, int quantity, Money unitPrice)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("order.line.add.locked", "已提交订单不能追加商品。");
        if (quantity <= 0)
            throw new DomainException("order.line.quantity", "数量必须为正整数。");

        var existing = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
        }
        else
        {
            _lines.Add(new OrderLine(productId, quantity, unitPrice));
        }

        RecalculateTotal();
    }

    public void Cancel(string reason)
    {
        if (Status is OrderStatus.Shipped or OrderStatus.Completed)
            throw new DomainException("order.cancel.tooLate", "订单已发货，无法取消。");

        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new OrderCancelledEvent(Id, reason));
    }

    private void RecalculateTotal()
    {
        TotalAmount = _lines.Aggregate(
            Money.Zero("CNY"),
            (sum, line) => sum + line.Subtotal);
    }
}
```

**关键点**：

1. `sealed` 默认加上，聚合根不应被继承
2. 集合用 `private readonly List<T>`，对外暴露 `IReadOnlyCollection<T>`
3. 所有属性 `private set`，修改只通过方法
4. 工厂方法 `Create` 封装创建逻辑，保证对象始终处于有效状态
5. 业务方法先校验不变量（抛 `DomainException`），再修改状态，最后 `AddDomainEvent`
6. `Money`、`Address` 等用值对象，不用裸 `decimal`、`string`

## 四、值对象编写模板

```csharp
using Kun.Core.Domain;

namespace MyService.Domain.SharedKernel;

public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length != 3)
            throw new ArgumentException("Currency 必须为 ISO 4217 三字母代码。", nameof(currency));

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException("不能相加不同币种。");
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator *(Money m, int factor) => new(m.Amount * factor, m.Currency);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}
```

**关键点**：

1. 不可变：所有属性 `get;` 无 `set`
2. 构造器中校验（值对象在创建瞬间就必须有效）
3. 运算符重载返回新实例，不修改自己
4. `GetEqualityComponents` 按顺序 yield 所有组成字段

## 五、领域事件

```csharp
namespace MyService.Domain.Orders.Events;

public sealed record OrderPlacedEvent(
    Guid OrderId,
    Guid CustomerId,
    Money TotalAmount,
    DateTime OccurredAt) : IDomainEvent
{
    public OrderPlacedEvent(Guid orderId, Guid customerId, Money totalAmount)
        : this(orderId, customerId, totalAmount, DateTime.UtcNow) { }
}
```

**关键点**：

1. 用 `record` —— 值相等、便于测试
2. 命名：过去式 + `Event` 后缀
3. 领域事件**不穿越进程边界**；需要跨服务时在事件处理器中转换为 `IIntegrationEvent`（参见 `kun-outbox-events`）
4. 领域事件由聚合根通过 `AddDomainEvent` 添加，UoW 在 `SaveChanges` 时统一收集并写入 Outbox

## 六、EF Core 持久化映射（基础设施层）

聚合根本身不带任何映射属性。映射写在基础设施层：

```csharp
// MyService.Infrastructure/Persistence/Configurations/OrderConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyService.Domain.Orders;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.CustomerId).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);

        // 值对象使用 OwnsOne 映射为同表列
        builder.OwnsOne(o => o.TotalAmount, total =>
        {
            total.Property(m => m.Amount).HasColumnName("total_amount").HasPrecision(18, 2);
            total.Property(m => m.Currency).HasColumnName("total_currency").HasMaxLength(3);
        });

        // 子实体集合
        builder.OwnsMany(o => o.Lines, lines =>
        {
            lines.ToTable("order_lines");
            lines.WithOwner().HasForeignKey("order_id");
            lines.Property<Guid>("Id");
            lines.HasKey("Id");
            // 行内值对象 UnitPrice
            lines.OwnsOne(l => l.UnitPrice);
        });

        // 软删除全局过滤器
        builder.HasQueryFilter(o => !o.IsDeleted);

        // 领域事件不持久化
        builder.Ignore(o => o.DomainEvents);
    }
}
```

## 七、自定义 Specification

```csharp
using Kun.Infrastructure.Abstractions;

public sealed class ActiveOrdersByCustomerSpec : Specification<Order>
{
    public ActiveOrdersByCustomerSpec(Guid customerId, int pageIndex, int pageSize)
    {
        AddCriteria(o => o.CustomerId == customerId
                      && o.Status != OrderStatus.Cancelled);
        // IsDeleted 由全局过滤器处理，无需重复
        ApplyOrderByDescending(o => o.CreatedAt);
        ApplyPaging(pageIndex * pageSize, pageSize);
        AddInclude(nameof(Order.Lines));
    }
}
```

业务 Handler 使用：

```csharp
var spec = new ActiveOrdersByCustomerSpec(customerId, page, size);
var orders = await _orderRepo.ListAsync(spec, ct);
var total  = await _orderRepo.CountAsync(spec, ct);
```

## 八、聚合边界的划分原则

新设计一个聚合时问自己三个问题：

1. **这个聚合的一致性规则是什么？**（不变量——什么情况下数据一定不能违反）
2. **这些规则涉及哪些对象？**（涉及的都应在同一聚合内）
3. **其他聚合引用它时只需要 Id 吗？**（是 → 独立聚合；否 → 可能要合并或重新设计）

**反模式**：把整张业务数据库塞进一个聚合。聚合应当尽量小，聚合之间通过 **Id 引用**（不是对象引用）和 **领域事件/集成事件** 协作。

## 九、领域异常

```csharp
namespace Kun.Core.Domain;

public sealed class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}
```

领域方法中对**违反不变量的非法操作**才抛 `DomainException`。对业务可预期的失败（如"库存不足"），应当返回 `Result<T>` 在 Command Handler 中处理，而非抛异常。

## 十、编写检查清单

生成聚合根/实体/值对象代码前，确认：

- [ ] 放在 `Domain/` 目录，无 ORM/JSON/验证属性
- [ ] 聚合根 `sealed`，继承 `AggregateRoot<TKey>` 或 `AggregateRoot<TKey, TUserKey>`
- [ ] 属性 `private set`，只通过方法修改
- [ ] 集合 `private readonly List<T>`，对外 `IReadOnlyCollection<T>`
- [ ] 工厂方法 `Create`（或业务含义明确的名字），不用 public 构造器
- [ ] 业务方法先校验再变更，最后 `AddDomainEvent`
- [ ] 金额、地址等用 `ValueObject`，不用裸基础类型
- [ ] 领域事件命名过去式 + `Event`，用 `record`
- [ ] 持久化映射写在 `IEntityTypeConfiguration<T>` 里，放基础设施层
