# Command 校验指南（Kun.Validation）

> 怎么在一个 Command/Query Handler 上挂校验，并且让失败以 `Result.Fail(Error.Validation(...))` 而不是异常的形式流到调用方。
>
> 参考：`docs/architecture/ADR-011-Result-async-extensions.md` · `docs/architecture/ADR-014-Validation-Result-behavior.md` · `src/Kun.Validation/AGENTS.md`

## 适用对象

在 `neuroagent-csharp` 或其他下游服务里新加一条 Command/Query，打算：

- 用 `IRequest<Result>` 或 `IRequest<Result<TValue>>` 返回业务结果（而不是抛 `ValidationException` 让 ASP.NET / gRPC 中间件翻译）；
- 在 Handler 之外把"请求形状合法性"（非空、长度、枚举范围等）挡住；
- 写单元测试时能直接断言 `result.Error.Code == "request.invalid"`，不需要 `Assert.ThrowsAsync<ValidationException>`。

如果你只写内部小工具、Handler 返回 `TResponse` 且可以接受异常语义，见下方 `ValidationPipelineBehavior` 路径；否则默认选 Result 路径。

## 选型决策树

Kun.Validation 目前出三个 Pipeline Behavior，按 Handler 响应类型二选一：

| Handler 响应签名 | 选的 Behavior | 失败时的行为 | 注册扩展 |
|------|------|------|------|
| `IRequest<TResponse>`（非 Result） | `ValidationPipelineBehavior<TRequest, TResponse>` | 抛 `ValidationException` | `AddValidationBehavior<TRequest, TResponse>()` |
| `IRequest<Result<TValue>>` | `ValidationResultPipelineBehavior<TRequest, TValue>` | 返回 `Result<TValue>.Fail(Error.Validation("request.invalid", …))` | `AddValidationResultBehavior<TRequest, TValue>()` |
| `IRequest<Result>`（无返回值命令） | `ValidationResultPipelineBehavior<TRequest>` | 返回 `Result.Fail(Error.Validation("request.invalid", …))` | `AddValidationResultBehavior<TRequest>()` |

> **铁律**：同一 `TRequest` 不要同时注册抛出版 + Result 版。Pipeline 会按注册顺序串成链，第二个 Behavior 跑在第一个已经翻译过的结果上，产生双层包装的校验消息。Kun 不在运行时拦截该错配（成本高、场景罕见），只靠 Code Review + 这份文档挡。

## 例 1：`Result<TValue>` 命令

场景：下单命令 `CreateOrderCommand → Result<OrderDto>`，两个校验器（数量 > 0、产品 ID 不为空）。

### Command + Validator

```csharp
public sealed record CreateOrderCommand(Guid ProductId, int Quantity)
    : IRequest<Result<OrderDto>>;

public sealed class CreateOrderQuantityValidator : IValidator<CreateOrderCommand>
{
    public ValueTask<ValidationResult> ValidateAsync(
        CreateOrderCommand instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.Quantity > 0) return ValueTask.FromResult(ValidationResult.Success);

        return ValueTask.FromResult(ValidationResult.Failure(
        [
            new ValidationError(
                PropertyName: nameof(instance.Quantity),
                Code: "order.quantity.greaterThanZero",
                Message: "Quantity must be greater than 0."),
        ]));
    }
}

public sealed class CreateOrderProductIdValidator : IValidator<CreateOrderCommand>
{
    public ValueTask<ValidationResult> ValidateAsync(
        CreateOrderCommand instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.ProductId != Guid.Empty) return ValueTask.FromResult(ValidationResult.Success);

        return ValueTask.FromResult(ValidationResult.Failure(
        [
            new ValidationError(
                PropertyName: nameof(instance.ProductId),
                Code: "order.productId.required",
                Message: "ProductId is required."),
        ]));
    }
}
```

错误码遵循 `{aggregate}.{field}.{constraint}`（`src/Kun.Validation/AGENTS.md` §Common Patterns）。

### DI 注册

```csharp
services.AddValidator<CreateOrderCommand, CreateOrderQuantityValidator>();
services.AddValidator<CreateOrderCommand, CreateOrderProductIdValidator>();

services.AddKunMediator(mediator =>
{
    mediator.AddValidationResultBehavior<CreateOrderCommand, OrderDto>();
    // mediator.AddRequestHandler<...>() — 由源生成器自动填
});
```

`AddValidator<,>` 挂在 `IServiceCollection`，`AddValidationResultBehavior<,>` 挂在 `KunMediatorBuilder`——分开是因为 Validator 是按请求类型注册的 transient，Behavior 是 Mediator pipeline 链上的一环。

### Handler

Handler 直接写成功路径；Validator 失败时 Behavior 已经短路，Handler 不会被调到。

```csharp
public sealed class CreateOrderCommandHandler
    : IRequestHandler<CreateOrderCommand, Result<OrderDto>>
{
    public async ValueTask<Result<OrderDto>> Handle(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // 走到这里时 Quantity > 0 且 ProductId != Guid.Empty 已成立。
        var product = await _products.GetAsync(request.ProductId, cancellationToken);
        if (product is null)
            return Result<OrderDto>.Fail(Error.NotFound("product.notFound", "Product not found."));

        var order = await _orders.CreateAsync(product, request.Quantity, cancellationToken);
        return Result<OrderDto>.Ok(OrderDto.From(order));
    }
}
```

### 测试

```csharp
[Fact]
public async Task Validator_fails_returns_request_invalid()
{
    var services = new ServiceCollection();
    services.AddValidator<CreateOrderCommand, CreateOrderQuantityValidator>();
    services.AddKunMediator(m =>
    {
        m.AddRequestHandler<CreateOrderCommand, Result<OrderDto>, CreateOrderCommandHandler>();
        m.AddValidationResultBehavior<CreateOrderCommand, OrderDto>();
    });
    var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

    var result = await mediator.Send(new CreateOrderCommand(Guid.NewGuid(), 0), CancellationToken.None);

    Assert.True(result.IsFailure);
    Assert.Equal("request.invalid", result.Error.Code);
    Assert.Equal(ErrorType.Validation, result.Error.Type);
    Assert.Contains("greater than 0", result.Error.Message);
}
```

更多断言帮助（`ResultAssertions.ShouldBeFailure(...)` 等）见 `src/Kun.Testing/AGENTS.md`。

## 例 2：`Result` 命令（无返回值）

场景：删除用户命令 `DeleteUserCommand → Result`，一个校验器（`UserId` 非空）。适用于幂等写命令、删除、标记为已读等"跑完了就行、没什么可返回"的业务。

```csharp
public sealed record DeleteUserCommand(Guid UserId) : IRequest<Result>;

public sealed class DeleteUserValidator : IValidator<DeleteUserCommand>
{
    public ValueTask<ValidationResult> ValidateAsync(
        DeleteUserCommand instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.UserId != Guid.Empty) return ValueTask.FromResult(ValidationResult.Success);

        return ValueTask.FromResult(ValidationResult.Failure(
        [
            new ValidationError(
                PropertyName: nameof(instance.UserId),
                Code: "user.userId.required",
                Message: "UserId is required."),
        ]));
    }
}

// DI：
services.AddValidator<DeleteUserCommand, DeleteUserValidator>();
services.AddKunMediator(m =>
{
    m.AddValidationResultBehavior<DeleteUserCommand>();  // 注意：只有一个类型参数
});

// Handler：
public sealed class DeleteUserCommandHandler
    : IRequestHandler<DeleteUserCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteUserCommand r, CancellationToken ct)
    {
        var user = await _users.GetAsync(r.UserId, ct);
        if (user is null) return Result.Fail(Error.NotFound("user.notFound", "User not found."));
        await _users.DeleteAsync(user, ct);
        return Result.Ok();
    }
}
```

`AddValidationResultBehavior<TRequest>()`（单参数）闭合在 `IRequest<Result>` 上；`AddValidationResultBehavior<TRequest, TValue>()`（双参数）闭合在 `IRequest<Result<TValue>>` 上。名字一样，只靠类型参数数量区分。

## 反模式

### 1. 同一 `TRequest` 注册两个 Behavior

```csharp
// ❌ 不要这样
m.AddValidationBehavior<CreateOrderCommand, Result<OrderDto>>();          // 抛出版
m.AddValidationResultBehavior<CreateOrderCommand, OrderDto>();            // Result 版
```

Pipeline 会串成链：第一个 Behavior 跑完（抛出）→ 第二个拿不到 `next()` 的返回 → 要么二次抛 `ValidationException`，要么产生双层包装的 `request.invalid` 消息。跑不出来预期结果，IDE 也不会警告。

### 2. 在 `Result`-Handler 里 `throw new ValidationException(...)`

```csharp
// ❌ 不要这样
public async ValueTask<Result<OrderDto>> Handle(...)
{
    if (request.Quantity < 0)
        throw new ValidationException(/* ... */);   // 业务层不该抛校验异常
    // ...
}
```

把校验放进 Validator；Handler 里用 `Result<TValue>.Fail(Error.Validation(...))` 直接返回。`kun/CLAUDE.md` §Architectural constraints："Business failures use `Result` / `Result<T>`, not exceptions." 抛异常会破坏下游 `Match / Bind / Tap` 组合链（ADR-011）。

### 3. 在 Validator 里调数据库 / 外部服务

```csharp
// ❌ 不要这样
public async ValueTask<ValidationResult> ValidateAsync(CreateOrderCommand r, CancellationToken ct)
{
    var product = await _db.Products.FindAsync(r.ProductId, ct);   // 跨层了
    if (product is null) return ValidationResult.Failure(/* ... */);
    // ...
}
```

Validator 只处理"请求形状合法性"（类型、范围、非空、正则）。凡是需要外部状态（"ProductId 对应的产品是否存在"、"该用户是否有权限"）的判断都是语义校验，归 Handler。Validator 纯函数化保证：AOT 友好、单元测试不需要 mock 任何 DbContext、Pipeline 中多个 Validator 可以并行跑。

## 排错

- **Behavior 没生效**：检查 `AddKunMediator(m => …)` 里是否真的调了 `AddValidation…Behavior`；源生成器只注册 Handler 本身，Behavior 需要显式挂到 builder。
- **打了 `AddValidator<,>()` 但 Validator 没跑**：确认 Validator 实现了 `IValidator<T>`（not `IValidator<TRequest, TResponse>`），且 `T` 精确匹配 Command 类型（`CreateOrderCommand` 不是 `object`）。
- **`Result.Error.Message` 为空**：Validator 返回了 `ValidationResult.Failure(errors)`，但 `errors` 数组里每个 `ValidationError.Message` 是空的。Behavior 用 `string.Join("; ", errs.Select(e => e.Message))`，空消息会拼出空字符串。

## 相关

- [ADR-011 Result async extensions](../architecture/ADR-011-Result-async-extensions.md)—`Result<T>` 的 `Map/Bind/Tap/Ensure/Match` 组合面。
- [ADR-014 Validation Result behavior](../architecture/ADR-014-Validation-Result-behavior.md)—两个 Result 版 Behavior 的设计决策。
- [src/Kun.Validation/AGENTS.md](../../src/Kun.Validation/AGENTS.md)—Validator 规范、错误码命名模式。
- [src/Kun.Testing/AGENTS.md](../../src/Kun.Testing/AGENTS.md)—`ResultAssertions.ShouldBeFailure` 等断言帮助。
- [docs/architecture/when-to-use-currentuser.md](../architecture/when-to-use-currentuser.md)—Kun 选型指南的样板格式，本文跟它的结构一致。
