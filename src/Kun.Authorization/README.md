# Kun.Authorization

仅提供权限、数据与字段策略端口；宿主自行实现 `IPermissionChecker` 等接口。主体包含 ID、租户与类型，可解释判定包含 allow、原因和命中策略。

- HTTP：`services.AddKunAuthorization()`，端点调用 `.RequireKunPermission("sample:item:read")`。
- Mediator：命令实现 `IPermissionProtectedRequest`，通过静态 `RequiredPermissions` 声明权限；注册 `.AddPermissionBehavior<TRequest, Result<T>>(Result<T>.Fail)`。静态声明不能由请求 JSON 改写。
- gRPC：服务或方法加 `[RequiresPermission("sample:item:read")]`；`AddKunGrpc()` 自动装配四种调用形态的权限拦截器。

三个入口在缺少引擎时拒绝，且在 handler 前判定。基础 HTTP/Mediator 适配器沿用现有用户身份上下文；服务或 Agent 的复杂主体与委托策略由平台身份及权限实现补充。权限集合不从令牌直接推导。

设计与兼容说明见 [ADR-016](../../docs/architecture/ADR-016-platform-ports.md)。
