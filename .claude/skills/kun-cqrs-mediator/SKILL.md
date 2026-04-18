---
name: kun-cqrs-mediator
description: Kun Framework 的 CQRS 与 Mediator 编码规范。当在 Kun 代码库中编写 Command、Query、Handler、Pipeline Behavior，或把 HTTP Minimal API / gRPC 端点接入 Kun.Mediator 时必须使用此技能。包括但不限于：新增业务命令（如 CreateOrderCommand）、新增查询（如 GetOrderByIdQuery）、编写对应的 Handler、定义 Result 返回约定、添加横切关注点（日志、验证、事务、幂等）的 Behavior、把 gRPC 服务的 RPC 方法路由到 Mediator。即使用户只说"帮我写个创建订单的接口"或"加个查询 Handler"，也要使用本技能以确保遵循 Kun 的调度器约定与 AOT 要求。先读 kun-framework-conventions 再读本技能。
---

# Kun CQRS 与 Mediator 规范

## 一、核心原则

- **写读分离**：Command 走 EF Core（写侧）+ 聚合根 + 领域事件；Query 走 Dapper AOT（读侧）+ DTO 直接投影
- **Handler 返回 `Result<T>`**：业务失败用 `Result.Fail(Error.X)`，不抛异常
- **无反射调度**：Handler 通过 SourceGen 注册到 `FrozenDictionary`，禁用运行时反射
- **职责单一**：一个 Handler 只做一件事，跨服务协作用领域事件/集成事件/Saga

## 二、Command 编写模板

```csharp
using Kun.Core.Cqrs;
using Kun.Core.Results;

namespace Catalog.Application.Products.Commands;

// Command：用 record，值相等，便于测试
public sealed record CreateProductCommand(
    string Name,
    decimal Price,
    string Currency,
    int InitialStock) : ICommand<Result<Guid>>;

// Handler：单一职责，构造器注入依赖
public sealed class CreateProductCommandHandler
    : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IRepository<Product> _repo;

    public CreateProductCommandHandler(IRepository<Product> repo)
    {
        _repo = repo;
    }

    public async ValueTask<Result<Guid>> Handle(
        CreateProductCommand request,
        CancellationToken cancellationToken)
    {
        // 1) 业务规则校验（返回 Result 而非抛异常）
        if (request.InitialStock < 0)
            return Error.Validation("product.stock.negative", "初始库存不能为负。");

        // 2) 构造聚合根（通过工厂方法，不用 new）
        var product = Product.Create(
            request.Name,
            new Money(request.Price, request.Currency),
            request.InitialStock);

        // 3) 仅添加到仓储，不调用 SaveChanges
        //    —— UoW Pipeline Behavior 会在 Handler 成功返回后统一提交
        await _repo.AddAsync(product, cancellationToken);

        return product.Id;
    }
}
```

**关键点**：

1. Command 用 `record`，不可变
2. 命名：动词 + `Command` 后缀
3. Handler `sealed`，继承 `IRequestHandler<TRequest, TResponse>`
4. **Handler 不调用 `SaveChangesAsync`**——由 `UnitOfWorkBehavior` 在 Handler 成功后自动调用（Outbox 事务由它保证）
5. 业务失败返回 `Error.X`，不抛
6. 参数校验在 Handler 中做（或通过 `ValidationBehavior` + `Kun.Validation` SourceGen 前置拦截）

## 三、Query 编写模板（走读侧 Dapper AOT）

```csharp
using Kun.Core.Cqrs;
using Kun.Core.Results;

namespace Catalog.Application.Products.Queries;

public sealed record GetProductByIdQuery(Guid ProductId) : IQuery<Result<ProductDetailDto>>;

public sealed record ProductDetailDto(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    int StockQuantity,
    DateTime CreatedAt);

public sealed class GetProductByIdQueryHandler
    : IRequestHandler<GetProductByIdQuery, Result<ProductDetailDto>>
{
    private readonly IProductReadService _reader;  // Dapper AOT 实现

    public GetProductByIdQueryHandler(IProductReadService reader) => _reader = reader;

    public async ValueTask<Result<ProductDetailDto>> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        var dto = await _reader.GetByIdAsync(request.ProductId, cancellationToken);

        return dto is null
            ? Error.NotFound("product.notFound", $"找不到 Id={request.ProductId} 的商品。")
            : dto;
    }
}
```

**Query 的约定**：

- 绝不调用仓储、不加载聚合根（避免无谓的变更追踪开销）
- 直接返回 DTO（不是领域对象）
- 走 Dapper AOT，SQL 显式写，看得见、可优化

## 四、Read Service 编写（Dapper AOT）

```csharp
using Dapper;

namespace Catalog.Infrastructure.ReadSide;

public interface IProductReadService
{
    Task<ProductDetailDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProductListItemDto>> ListAsync(int page, int size, CancellationToken ct);
}

public sealed class ProductReadService : IProductReadService
{
    private readonly IReadDbConnectionFactory _factory;

    public ProductReadService(IReadDbConnectionFactory factory) => _factory = factory;

    // [DapperAot] 触发 Dapper 源生成器，编译期生成零反射映射代码
    [DapperAot]
    public async Task<ProductDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _factory.CreateAsync(ct);
        const string sql = """
            SELECT id, name, price, currency, stock_quantity, created_at
            FROM products
            WHERE id = @Id AND is_deleted = FALSE
            """;
        return await conn.QuerySingleOrDefaultAsync<ProductDetailDto>(sql, new { Id = id });
    }

    [DapperAot]
    public async Task<IReadOnlyList<ProductListItemDto>> ListAsync(int page, int size, CancellationToken ct)
    {
        await using var conn = await _factory.CreateAsync(ct);
        const string sql = """
            SELECT id, name, price, currency, stock_quantity
            FROM products
            WHERE is_deleted = FALSE
            ORDER BY created_at DESC
            LIMIT @Size OFFSET @Offset
            """;
        var items = await conn.QueryAsync<ProductListItemDto>(
            sql, new { Size = size, Offset = page * size });
        return items.AsList();
    }
}
```

**关键点**：

1. 每个方法标 `[DapperAot]`，启用源生成映射
2. SQL 放在 raw string literal `"""..."""`，避免 SQL 注入要靠参数化（`@Id`、`@Offset`）
3. 租户隔离：多租户场景下 WHERE 需显式加 `AND tenant_id = @TenantId`（从 `ITenantContext` 取）

## 五、Pipeline Behaviors（横切关注点）

Kun.Mediator 支持围绕 Handler 的 Behavior 管道。执行顺序（从外到内）：

```
Logging → Tracing → Validation → Authorization → UnitOfWork → Handler
```

### UnitOfWorkBehavior（框架提供）

Handler 成功返回后自动调用 `IUnitOfWork.SaveChangesAsync`。失败则不提交，聚合根变更回滚。Outbox 消息在同一事务内落库。

### ValidationBehavior（框架提供）

对 Command 进行 `Kun.Validation` SourceGen 生成的验证器检查。验证失败直接返回 `Result.Fail(Error.Validation(...))`，不调用 Handler。

### 自定义 Behavior 模板

```csharp
using Kun.Mediator;
using Microsoft.Extensions.Logging;

namespace MyService.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Handling {Request}", requestName);

        var start = TimeProvider.System.GetTimestamp();
        try
        {
            return await next();
        }
        finally
        {
            var elapsed = TimeProvider.System.GetElapsedTime(start);
            _logger.LogInformation("Handled {Request} in {ElapsedMs}ms",
                requestName, elapsed.TotalMilliseconds);
        }
    }
}
```

## 六、HTTP Minimal API 接入 Mediator

```csharp
// Program.cs 中的 MapXxxEndpoints
app.MapPost("/api/products", async (
    CreateProductCommand cmd,
    ISender mediator,
    CancellationToken ct) =>
{
    var result = await mediator.Send(cmd, ct);
    return result.IsSuccess
        ? Results.Created($"/api/products/{result.Value}", result.Value)
        : result.Error.ToProblemDetails();  // Kun.AspNetCore 提供扩展
});

app.MapGet("/api/products/{id:guid}", async (
    Guid id,
    ISender mediator,
    CancellationToken ct) =>
{
    var result = await mediator.Send(new GetProductByIdQuery(id), ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : result.Error.ToProblemDetails();
});
```

**注意**：Minimal API Lambda 中不要直接写业务逻辑；永远是 `收参 → mediator.Send → 映射结果`。

## 七、gRPC 端点接入 Mediator

```csharp
public sealed class CatalogGrpcService : CatalogGrpc.CatalogGrpcBase
{
    private readonly ISender _mediator;

    public CatalogGrpcService(ISender mediator) => _mediator = mediator;

    public override async Task<ProductResponse> GetProductById(
        GetProductRequest request, ServerCallContext context)
    {
        var query = new GetProductByIdQuery(Guid.Parse(request.Id));
        var result = await _mediator.Send(query, context.CancellationToken);

        // EnsureSuccess() 在失败时抛 ResultFailureException
        // 由 KunResultToRpcInterceptor 捕获并翻译为 RpcException
        var dto = result.EnsureSuccess();
        return new ProductResponse
        {
            Id = dto.Id.ToString(),
            Name = dto.Name,
            Price = (double)dto.Price,
            Currency = dto.Currency
        };
    }
}
```

## 八、Handler 注册（AOT 关键）

Kun.Mediator 通过 SourceGen 在编译期扫描所有 `IRequestHandler<,>` 实现，生成 `FrozenDictionary<Type, HandlerFactory>` 注册代码。业务项目只需在 `Program.cs` 中：

```csharp
builder.Services.AddKunMediator(typeof(Program).Assembly);
```

**这行代码在编译期会被 SourceGen 扩展为所有 Handler 的具体注册**，运行时零反射。绝对不要自己写 `AppDomain.GetAssemblies().GetTypes()` 式的扫描代码。

## 九、常见反模式

❌ **Handler 直接调 `SaveChangesAsync`**
→ UnitOfWorkBehavior 已处理；自己调会破坏 Outbox 事务边界

❌ **Command Handler 返回聚合根本身**
→ 返回 `Result<TDto>` 或 `Result<Guid>`（Id）；聚合根是领域对象，不是 API 契约

❌ **Query Handler 使用 `IRepository<T>` 加载聚合根再投影**
→ 走 Dapper AOT 直接 SQL 投影；EF Core 的变更追踪在 Query 路径是纯开销

❌ **在 Handler 中 `throw new Exception("业务失败")`**
→ 返回 `Error.X`；异常只用于系统性故障

❌ **Command 和 Query 混在一个类里**
→ 严格分离；Command 在 `/Commands/`，Query 在 `/Queries/`

❌ **在 Behavior 中读取 HttpContext.User**
→ 通过 `ICurrentUser`（从 `Kun.Core.Security`）注入；Behavior 不应依赖 HTTP

❌ **多 Handler 之间通过静态字段共享状态**
→ 任何跨 Handler 的协作都必须通过领域事件/集成事件/Saga

## 十、编写检查清单

编写一个 Command/Query 时：

- [ ] Command 放 `/Commands/`，Query 放 `/Queries/`
- [ ] Command/Query 用 `record`，Handler 用 `sealed class`
- [ ] 返回类型是 `Result<T>` 或 `Result`
- [ ] Handler 只依赖抽象（仓储、ReadService、Mediator），不 `new DbContext`
- [ ] Handler 不调 `SaveChangesAsync`
- [ ] 业务失败返回 `Error.X`，不抛异常
- [ ] Query 走 Dapper AOT 的 `[DapperAot]` 方法
- [ ] 多租户场景下查询 SQL 包含 `tenant_id` 过滤
- [ ] HTTP/gRPC 端点中没有业务逻辑，只做参数映射与 `mediator.Send`
