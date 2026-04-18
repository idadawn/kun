# 模块地图

## 依赖关系图

```mermaid
graph TD
    Core[Kun.Core<br/>领域抽象/CQRS 接口/Result] --> Mediator[Kun.Mediator<br/>无反射调度器]
    Core --> Infra[Kun.Infrastructure<br/>仓储/UoW/规约/租户抽象]
    Core --> EventBus[Kun.EventBus<br/>事件分发/Outbox 抽象]
    Core --> Security[Kun.Security<br/>S2S JWT/租户上下文]
    Core --> Validation[Kun.Validation<br/>SourceGen 验证器]

    Infra --> EfCore[Kun.EntityFrameworkCore<br/>写侧持久化/Outbox 实现]
    Infra --> Dapper[Kun.Dapper<br/>读侧查询]

    EventBus --> Saga[Kun.Saga<br/>Saga 编排器/补偿/超时]
    EventBus --> RabbitMq[Kun.EventBus.RabbitMQ<br/>集成事件传输]

    Mediator --> AspNet[Kun.AspNetCore<br/>Minimal API/Pipeline Behaviors]
    Mediator --> Grpc[Kun.Grpc<br/>拦截器/Result 映射]

    Security --> Gateway[Kun.Gateway<br/>YARP/S2S 签发/限流]

    style Core fill:#e1f5ff
    style Mediator fill:#e1f5ff
    style Security fill:#ffe1e1
    style Saga fill:#fff4d6
```

## 分层规则

```
┌─────────────────────────────────────────────────────────┐
│  Host Layer                                             │
│  Kun.AspNetCore / Kun.Grpc / Kun.Gateway                │
├─────────────────────────────────────────────────────────┤
│  Infrastructure Layer                                   │
│  Kun.EntityFrameworkCore / Kun.Dapper                   │
│  Kun.EventBus.RabbitMQ / Kun.Saga                       │
├─────────────────────────────────────────────────────────┤
│  Abstraction Layer                                      │
│  Kun.Mediator / Kun.Infrastructure                      │
│  Kun.EventBus / Kun.Security / Kun.Validation           │
├─────────────────────────────────────────────────────────┤
│  Core Layer                                             │
│  Kun.Core (zero dependencies)                           │
└─────────────────────────────────────────────────────────┘
```

## 典型请求调用路径（HTTP → DB → 事件）

```
HTTP Request
    │
    ▼
Kun.Gateway                      [签发 S2S JWT]
    │
    ▼
Kun.AspNetCore / Minimal API     [校验 S2S JWT / 注入 ITenantContext]
    │
    ▼
Kun.Mediator.Send(Command)       [Pipeline: Logging → Validation → Auth → Handler]
    │
    ▼
Command Handler                  [纯 C#，依赖 IRepository<T>]
    │
    ▼
Kun.EntityFrameworkCore          [聚合根 + Outbox 同事务落库]
    │
    ▼
PostgreSQL                       [SaveChanges = 原子事务]
    │
    ▼
Kun.EventBus OutboxPublisher     [扫描 pending 消息 → RabbitMQ]
    │
    ▼
Kun.EventBus.RabbitMQ            [传递 W3C Trace Context]
    │
    ▼
下游服务消费端                    [IdempotentConsumerDecorator → Handler]
```

## 典型查询路径（HTTP → DB 只读）

```
HTTP Request
    │
    ▼
Kun.AspNetCore / Minimal API
    │
    ▼
Kun.Mediator.Send(Query)
    │
    ▼
Query Handler
    │
    ▼
Kun.Dapper (读侧连接)            [AOT 源生成的映射代码]
    │
    ▼
PostgreSQL (只读副本 / 读库)
```

## Saga 路径（跨服务长事务）

```
Command Handler
    │
    ▼ StartSagaAsync(ctx)
    │
Kun.Saga Orchestrator            [持久化 SagaInstance + 首步入队]
    │                               (与业务同事务)
    ▼
Outbox (step message)
    │
    ▼
SagaStepExecutor (BackgroundService)
    │
    ▼  step.ExecuteAsync()
    │
远程服务 / 本地 Handler
    │
    ▼  成功 → 下一步                  失败 → CompensateAsync (反向)
```
