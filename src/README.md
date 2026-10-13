# src

Kun Framework 的核心源码。所有项目都继承 `/Directory.Build.props` 与 `/Directory.Packages.props` 的配置。

## 模块速查

| 模块 | 职责 | AOT Ready |
| :--- | :--- | :---: |
| [Kun.Core](Kun.Core/) | 领域抽象、CQRS 接口、Result、`ITenantContext`（零第三方依赖） | ✅ |
| [Kun.Mediator](Kun.Mediator/) | `FrozenDictionary` 无反射调度、Pipeline Behaviors | ✅ |
| [Kun.Infrastructure](Kun.Infrastructure/) | 仓储/UoW/规约接口、`IProcessedMessageStore` | ✅ |
| [Kun.EntityFrameworkCore](Kun.EntityFrameworkCore/) | 写侧 EF Core 实现、Outbox 落库、租户过滤器 | ⚠️ trim-safe |
| [Kun.Dapper](Kun.Dapper/) | 读侧 Dapper AOT 封装 | ✅ |
| [Kun.EventBus.Abstractions](Kun.EventBus.Abstractions/) | 稳定事件标记与 JSON 注册端口（只依赖 Core） | ✅ |
| [Kun.EventBus](Kun.EventBus/) | 领域/集成事件抽象、Outbox 发布器 | ✅ |
| [Kun.EventBus.RabbitMQ](Kun.EventBus.RabbitMQ/) | RabbitMQ 传输、消费端幂等 | ✅ |
| [Kun.Saga](Kun.Saga/) | Saga Orchestration 编排器 | ✅ |
| [Kun.Security](Kun.Security/) | S2S JWT 签发/校验、租户上下文注入 | ✅ |
| [Kun.Validation](Kun.Validation/) | SourceGen 验证器 | ✅ |
| [Kun.AspNetCore](Kun.AspNetCore/) | Minimal API 扩展、Pipeline Behaviors | ✅ |
| [Kun.Grpc](Kun.Grpc/) | gRPC 拦截器、Result → RpcException 映射 | ✅ |
| [Kun.Gateway](Kun.Gateway/) | YARP 扩展、S2S 签发、限流 | ✅ |

## 开发规范

所有开发必须遵循根目录下 [`.claude/skills/`](../.claude/skills/) 的约定。核心约束：

- **Kun.Core 零第三方依赖**
- **领域层不得携带 ORM / JSON / 验证属性**
- **AOT 候选模块禁止运行时反射**
- **CI 强制 AOT 零警告发布**

详见 [CONTRIBUTING.md](../CONTRIBUTING.md) 与 [`docs/architecture/module-map.md`](../docs/architecture/module-map.md)。
