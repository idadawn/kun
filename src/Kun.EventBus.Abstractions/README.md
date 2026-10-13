# Kun.EventBus.Abstractions

用于只发布事件契约的包：稳定 `IntegrationEventIdAttribute`、`IEventTypeRegistrar`（含 `JsonTypeInfo<T>` 注册重载）与 `IEventTypeRegistry`。命名空间继续使用 `Kun.EventBus`，仅 NuGet 包引用变化。

本包只依赖 `Kun.Core`，不传递引入 Redis、RabbitMQ、DI 或后台宿主。`Kun.Core.Domain.IIntegrationEvent` 继续由 Core 定义，本包通过传递依赖与类型转发导出同一类型，保持既有契约身份及 Core 的零外部依赖；不复制接口，也不让 Core 反向依赖事件包。

契约包只需引用本包，以源生成的 `JsonTypeInfo<T>` 调用 `IEventTypeRegistrar.Register<T>(stableId, typeInfo)`。应用宿主仍引用 `Kun.EventBus` 并通过 `AddKunEventBus` 装配实现。旧 `Kun.EventBus` 为三个迁移类型保留类型转发，原命名空间、签名和事件 ID 不变。

测试覆盖只引用抽象包的事件声明与源生成 JSON、无实现依赖的程序集闭包，以及从原程序集名称解析迁移类型的兼容性。发布时与其它 Kun 包使用同一版本。
