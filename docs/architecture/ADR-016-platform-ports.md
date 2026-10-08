# ADR-016 配置可靠性、权限端口与能力目录

状态：Accepted。日期：2026-10-09。执行依据：Qian 已接受的 ADR-006、ADR-007 与任务 S1-KUN-006；本文记录上游框架的落位，不引入新的权限模型。

## 决策

- 新增 `Kun.Authorization` 抽象包，仅定义主体、可解释判定、`IPermissionChecker`、`IDataPermissionFilter`、`IFieldPolicy`、命令静态权限声明与端点元数据，不提供 RBAC/ABAC 引擎或数据库实现。
- HTTP 经 `AddKunAuthorization()` 与 `RequireKunPermission()` 接入 ASP.NET Core 策略；Mediator 命令实现 `IPermissionProtectedRequest` 并装配 `AddPermissionBehavior`；gRPC 的四种调用形态读取 `[RequiresPermission]` 元数据。缺少引擎一律拒绝，判定先于 handler 与流首帧读取。身份认证由已有 Security/Identity 入口提供；平台策略与权限管理属于下游。
- `Kun.Mediator` 的 handler 注册可同时接收稳定能力 ID、请求和响应 `JsonTypeInfo`，经 `ICapabilityRegistry` 枚举 JSON Schema 2020-12。目录本身不等于调用授权；上层必须按权限与 scope 过滤后对外暴露。
- `AddKunOptions<T>` 在变更后重新创建和校验候选值，成功才替换当前值；拒绝时保留旧对象并增加 `kun.configuration.reload.rejected`，日志不写配置内容。关键配置维持 ValidateOnStart，可选配置可延后至首次读取。Schema 导出表达 JSON 形状；DataAnnotations 与自定义语义校验仍由 validator 执行。
- `Kun.Configuration.Apollo` 是独立 HTTP 配置源，支持 properties namespace 的有序叠加、长轮询、通知修订回传、AccessKey HMAC-SHA1、服务端故障转移与本地缓存。HMAC-SHA1 仅用于遵守 Apollo 既有协议，不用于新密码学设计。配置源不负责租户配置、secretRef 解析、发布审批或审计，这些由平台 Provider 负责。
- `KunExceptionHandler` 通过 `IProblemDetailsService` 输出，让宿主可以安装自定义错误写入器。它不再绕过宿主直接序列化 ProblemDetails。

## 依赖与兼容

`Kun.Authorization` 只依赖 `Kun.Core`；Mediator、ASP.NET Core 与 gRPC 向下依赖它。Apollo provider 仅依赖标准 Configuration/Logging 抽象，无平台依赖。两包均开启 AOT 分析并加入 CI 发布矩阵。

旧仓储接口仍有兼容实现，不能删掉后假装从未使用：以 `KUNLEGACY001` 标记废弃，保留 EF 与旧存储内部兼容代码；新下游代码以 Banned API 门禁拒绝旧入口。新存储包是 `Kun.Storage.Abstractions`，其中接口的实际全名为 `Kun.Storage.IFileStorage`；旧 `Kun.Storage` 包中的接口是 `Kun.Storage.Abstractions.IFileStorage`。

`AddKunDapper` / `AddKunDapperNpgsql` 新增 `Func<ReadDbOptions>` 重载，解决 init-only 属性无法在 Action 内赋值，同时保留原方法二进制签名。慢请求阈值使用 `PerformanceBehaviorOptions.ThresholdMs`，原构造器和 500ms 默认值不变。

## 验证与边界

配置测试覆盖非法值、类型绑定错误、拒绝指标、有效修订恢复、可选配置、Schema 实例验证。Apollo 测试通过替身 HTTP 协议覆盖签名、缓存、鉴权失败不回退、离线启动失败、快照校验、长轮询修订回传；这是协议集成验收，未声明已对接生产 Apollo。

权限测试覆盖缺失引擎、明确拒绝与允许，以及 HTTP/Mediator/gRPC 判定顺序；错误处理中测试宿主写入器真正接管响应。没有在框架中实现租户 RBAC、字段脱敏算法或行级 SQL 编译器。
