# Kun.EventBus.Abstractions

- 本包是事件契约入口，只允许依赖 BCL 与 Kun.Core；禁止引入传输、Redis、DI 或后台宿主依赖。
- 保持 `Kun.EventBus` 中既有类型的命名空间与签名；迁移后的类型由原 Kun.EventBus 程序集转发。
- IIntegrationEvent 复用 Kun.Core.Domain 中的唯一类型，不创建第二套标记，也不建立 Core 的反向依赖。
- 公共 API 登记 PublicAPI 文件；注册与序列化优先使用源生成 JsonTypeInfo。
