---
name: kun-testing
description: Kun Framework 的测试编写规范。当在 Kun 代码库中编写任何测试（领域单元测试、Mediator Handler 测试、Testcontainers 集成测试、Saga 编排测试、gRPC 契约测试、AOT 发布验证）时必须使用此技能。包括但不限于：测试聚合根业务方法、测试 Command/Query Handler、验证 Outbox 消息在事务中正确落库、验证 Saga 在步骤失败时反向补偿、验证租户隔离、验证 gRPC 契约不破坏、在 CI 中做 AOT publish 零警告验证。即使用户只说"给这个类加个测试"或"帮我验证一下这个流程"，也要使用本技能以确保选择了正确的测试层级、使用了 Testcontainers 而非 mock 一把梭、遵循了 AAA 结构与命名规范。先读 kun-framework-conventions 再读本技能。
---

# Kun 测试规范

## 一、四层测试金字塔

```
                    ▲
                   ╱ ╲           (4) AOT 发布验证（CI 中）
                  ╱───╲          —— dotnet publish -p:PublishAot=true 零警告
                 ╱     ╲
                ╱ 契约  ╲        (3) gRPC 契约测试
               ╱─────────╲       —— Proto 不破坏兼容性
              ╱           ╲
             ╱   集成测试   ╲      (2) Testcontainers 集成测试
            ╱───────────────╲    —— 真实 PG / RabbitMQ / Redis
           ╱                 ╲
          ╱   领域单元测试     ╲   (1) 纯内存单元测试
         ╱─────────────────────╲  —— 聚合根 / Handler / 值对象
```

**选择哪一层的判断**：

| 被测对象 | 层级 | 依赖 |
| :--- | :--- | :--- |
| 聚合根业务方法 | 单元 | 纯 C#，无 IO |
| 值对象相等性、运算 | 单元 | 纯 C# |
| Command Handler 业务分支 | 单元 | mock 仓储 |
| Query Handler SQL + DTO 映射 | 集成 | Testcontainers |
| UoW + Outbox 原子性 | 集成 | Testcontainers |
| RabbitMQ 发布/消费链路 | 集成 | Testcontainers |
| Saga 补偿流程 | 集成 | Testcontainers |
| 租户隔离 EF 过滤器 | 集成 | Testcontainers |
| gRPC 服务契约 | 契约 | Proto 生成客户端 |

## 二、通用约定

### 项目结构

```
tests/
├── Kun.Core.Tests/             # Kun.Core 的纯单元测试
├── Kun.Mediator.Tests/          # Kun.Mediator 的调度器测试
├── Kun.Saga.Tests/              # Kun.Saga 编排器单元测试
└── Kun.IntegrationTests/        # 跨模块 + Testcontainers
```

业务项目各自建 `{Project}.Tests` 与 `{Project}.IntegrationTests`。

### 技术栈

- **框架**：xUnit v3
- **断言**：FluentAssertions
- **Mock**：NSubstitute（AOT 友好，优先于 Moq）
- **容器**：Testcontainers.PostgreSql / Testcontainers.RabbitMq / Testcontainers.Redis

### 命名约定

方法名三段式：`MethodUnderTest_Scenario_ExpectedBehavior`

- `AddLine_WhenOrderSubmitted_ShouldThrowDomainException`
- `Handle_WithInvalidStock_ShouldReturnValidationError`
- `SaveChanges_WithDomainEvent_ShouldPersistOutboxMessage`

测试类命名：`{TypeUnderTest}Tests`，如 `OrderTests`、`CreateProductCommandHandlerTests`。

## 三、领域单元测试

**目标**：聚合根、值对象、领域事件。纯内存，毫秒级。

```csharp
using FluentAssertions;
using Xunit;

namespace Catalog.Domain.Tests.Products;

public class ProductTests
{
    [Fact]
    public void Create_WithValidInputs_ShouldRaiseProductCreatedEvent()
    {
        // Arrange & Act
        var product = Product.Create(
            name: "iPhone 17",
            price: new Money(9999m, "CNY"),
            initialStock: 100);

        // Assert
        product.Id.Should().NotBeEmpty();
        product.DomainEvents.Should()
            .ContainSingle(e => e is ProductCreatedEvent);
    }

    [Fact]
    public void Create_WithNegativeStock_ShouldThrowDomainException()
    {
        // Act
        var act = () => Product.Create("iPhone 17", new Money(9999m, "CNY"), initialStock: -1);

        // Assert
        act.Should().Throw<DomainException>()
            .Where(e => e.Code == "product.stock.negative");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ReduceStock_WithInvalidQuantity_ShouldThrowDomainException(int quantity)
    {
        var product = ProductFactory.CreateDefault(stock: 10);

        var act = () => product.ReduceStock(quantity);

        act.Should().Throw<DomainException>();
    }
}
```

**关键点**：

1. `[Fact]` 用于具体场景，`[Theory]` + `[InlineData]` 参数化
2. AAA（Arrange / Act / Assert）分块清晰
3. 使用 `Factory.CreateDefault(...)` 或 `Builder` 模式简化 Arrange
4. 领域单元测试**永远不 mock**——它们纯粹是业务逻辑验证

## 四、Handler 单元测试

**目标**：Command/Query Handler 的业务分支、错误处理。mock 掉 IO 依赖。

```csharp
using NSubstitute;
using FluentAssertions;
using Kun.Core.Results;
using Xunit;

namespace Catalog.Application.Tests.Products.Commands;

public class CreateProductCommandHandlerTests
{
    private readonly IRepository<Product> _repo = Substitute.For<IRepository<Product>>();

    [Fact]
    public async Task Handle_WithValidCommand_ShouldAddProductAndReturnId()
    {
        var handler = new CreateProductCommandHandler(_repo);
        var cmd = new CreateProductCommand("iPhone 17", 9999m, "CNY", 100);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _repo.Received(1).AddAsync(
            Arg.Is<Product>(p => p.Name == "iPhone 17"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNegativeStock_ShouldReturnValidationError()
    {
        var handler = new CreateProductCommandHandler(_repo);
        var cmd = new CreateProductCommand("iPhone 17", 9999m, "CNY", -1);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _repo.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }
}
```

## 五、Testcontainers 集成测试

**目标**：跨层路径、UoW + Outbox、Saga、租户隔离、真实 SQL。

### Fixture 骨架

```csharp
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

public sealed class KunTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("kun_test")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    public IServiceProvider Services { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKunMediator(typeof(CreateProductCommand).Assembly);
        services.AddKunEntityFrameworkCore(options =>
            options.UseNpgsql(PostgresConnectionString));
        services.AddKunEventBus().AddRabbitMQ(o => o.ConnectionString = RabbitMqConnectionString);

        Services = services.BuildServiceProvider();

        // 运行 Migrations
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }
}
```

### Outbox 原子性测试

```csharp
public class OutboxIntegrationTests : IClassFixture<KunTestFixture>
{
    private readonly KunTestFixture _fixture;

    public OutboxIntegrationTests(KunTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SaveChanges_WithDomainEvent_ShouldPersistOutboxMessageInSameTransaction()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Product>>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var product = Product.Create("iPhone 17", new Money(9999m, "CNY"), 100);

        // Act
        await repo.AddAsync(product);
        await uow.SaveChangesAsync();

        // Assert：Outbox 消息与产品在同一事务
        var outboxCount = await db.Set<OutboxMessage>()
            .CountAsync(m => m.EventTypeId == "product.created.v1");
        outboxCount.Should().Be(1);

        var savedProduct = await db.Set<Product>().FindAsync(product.Id);
        savedProduct.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveChanges_WhenPersistenceFails_ShouldNotWriteOutbox()
    {
        // Arrange：制造持久化失败（违反 NOT NULL）
        using var scope = _fixture.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var invalidProduct = Product.CreateInvalid();  // 故意构造失败场景
        db.Add(invalidProduct);

        // Act
        var act = () => uow.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();

        // Outbox 也不应有消息（同一事务回滚）
        var outboxCount = await db.Set<OutboxMessage>().CountAsync();
        outboxCount.Should().Be(0);
    }
}
```

## 六、Saga 集成测试（三条必测路径）

```csharp
public class OrderCreationSagaTests : IClassFixture<KunTestFixture>
{
    // Path 1: Happy Path
    [Fact]
    public async Task OrderSaga_WhenAllStepsSucceed_ShouldCompleteNormally()
    {
        using var scope = _fixture.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISagaOrchestrator>();

        var ctx = new OrderCreationSagaContext { /*...*/ };
        var sagaId = await orchestrator.StartAsync<OrderCreationSaga, OrderCreationSagaContext>(ctx);

        await _fixture.WaitForSagaCompletionAsync(sagaId, timeout: TimeSpan.FromSeconds(30));

        var instance = await _fixture.GetSagaInstanceAsync(sagaId);
        instance.State.Should().Be(SagaState.Completed);
    }

    // Path 2: Failure triggers compensation
    [Fact]
    public async Task OrderSaga_WhenPaymentFails_ShouldCompensateInventoryReservation()
    {
        // Arrange: make the payment step return a failure
        _fixture.ConfigurePaymentServiceToFail(Error.Conflict("payment.declined", "Card declined"));

        var sagaId = await _fixture.StartOrderSagaAsync();
        await _fixture.WaitForSagaCompletionAsync(sagaId);

        // Assert
        var instance = await _fixture.GetSagaInstanceAsync(sagaId);
        instance.State.Should().Be(SagaState.Compensated);
        instance.FailureReason.Should().Contain("payment.declined");

        // 下游收到了库存释放事件
        var releasedEvents = await _fixture.ConsumedEventsAsync<InventoryReleasedIntegrationEvent>();
        releasedEvents.Should().ContainSingle();
    }

    // Path 3: Timeout triggers compensation
    [Fact]
    public async Task OrderSaga_WhenStepTimesOut_ShouldCompensate()
    {
        _fixture.ConfigurePaymentServiceToDelay(TimeSpan.FromSeconds(60));  // 超过 Timeout

        var sagaId = await _fixture.StartOrderSagaAsync();
        await _fixture.WaitForSagaCompletionAsync(sagaId, timeout: TimeSpan.FromSeconds(45));

        var instance = await _fixture.GetSagaInstanceAsync(sagaId);
        instance.State.Should().Be(SagaState.Compensated);
        instance.FailureReason.Should().Contain("Timeout");
    }
}
```

## 七、gRPC 契约测试

目标：防止 Proto 改动破坏既有客户端。

```csharp
public class CatalogGrpcContractTests
{
    [Fact]
    public void Proto_ShouldContainAllRequiredMessages()
    {
        var descriptor = CatalogGrpc.Descriptor;

        descriptor.Services
            .Single(s => s.Name == "CatalogGrpc")
            .Methods.Select(m => m.Name)
            .Should().Contain(new[] { "GetProductById", "CreateProduct", "ListProducts" });
    }

    [Fact]
    public async Task GetProductById_WithValidId_ShouldReturnProduct()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var channel = GrpcChannel.ForAddress(factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var client = new CatalogGrpc.CatalogGrpcClient(channel);

        // Arrange: seed a product via the real DbContext
        var productId = await _fixture.SeedProductAsync("iPhone 17", 9999m);

        // Act
        var response = await client.GetProductByIdAsync(new GetProductRequest { Id = productId.ToString() });

        // Assert
        response.Id.Should().Be(productId.ToString());
        response.Name.Should().Be("iPhone 17");
    }
}
```

## 八、AOT 发布验证（CI）

CI 脚本已经内置，每个 PR 会对 AOT 候选模块跑：

```bash
dotnet publish src/Kun.Gateway -c Release -p:PublishAot=true /warnaserror
```

**本地验证**（提交前）：

```bash
dotnet publish src/Kun.Mediator -c Release -p:PublishAot=true
```

如果出现 `IL2026`、`IL3050` 等警告：

- `IL2026`：反射访问被裁剪 → 改用 `JsonSerializerContext` 或 `[DynamicallyAccessedMembers]`
- `IL3050`：运行时代码生成（如 `JsonSerializer.Serialize<T>(obj)`）→ 显式传 `JsonTypeInfo`

## 九、数据库测试数据管理

使用 **Respawn** 或手动 `TRUNCATE` 清理，不要 `EnsureDeleted + EnsureCreated`（太慢，每测试起容器太贵）。

```csharp
public async Task ResetDatabaseAsync()
{
    await _connection.OpenAsync();
    await _respawner.ResetAsync(_connection);
}
```

每个测试类用 `IClassFixture<KunTestFixture>` 共享容器，在类级别复用；每个测试方法前 `ResetDatabaseAsync` 清理。

## 十、常见反模式

❌ **Mock 一把梭写"测试"**
→ Mock 了 `IRepository`、Mock 了 `IUnitOfWork`、Mock 了 `IMediator`，最后测的只是"Mock 被调用了一次"。集成路径必须用 Testcontainers 真跑

❌ **单元测试断言内部实现**
→ "测试方法被调用一次"而不是"业务状态正确"。断言应该是对**可观察行为**：领域事件、返回值、状态变化

❌ **集成测试不重置数据库**
→ 测试顺序依赖、间歇性失败的根源

❌ **Saga 测试只测 Happy Path**
→ Saga 最值钱的代码是补偿；必须测失败分支

❌ **用 `Assert.True(result.IsSuccess)` 代替 FluentAssertions**
→ 失败时 FluentAssertions 给出的消息更友好

❌ **在测试里硬编码连接字符串**
→ 用 Testcontainers 的 `GetConnectionString()`，零配置、隔离好

❌ **把 Integration Test 当作 Unit Test 跑**
→ CI 中集成测试要在单独 job 跑（已在 `ci.yml` 中配置），不要和单元测试混成一个

## 十一、编写检查清单

提交新测试前：

- [ ] 选对了测试层级（单元 / 集成 / 契约）
- [ ] 命名遵循 `MethodUnderTest_Scenario_ExpectedBehavior`
- [ ] AAA 结构清晰
- [ ] 断言聚焦**可观察行为**，不是内部实现
- [ ] 集成测试使用 Testcontainers，每个测试前重置数据库
- [ ] Saga 测试覆盖 Happy / 失败 / 超时 三条路径
- [ ] 新的 AOT 候选代码，本地跑过 `dotnet publish -p:PublishAot=true` 零警告
