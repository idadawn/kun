# 贡献指南

感谢您对 **Kun Framework** 的关注！

## 📋 行为准则

本项目采用 [Contributor Covenant](https://www.contributor-covenant.org/) 行为准则。请保持专业、尊重与建设性。

## 🛠️ 开发环境

### 必需工具

- **.NET 10 SDK** (`10.0.100` 或更高) —— 版本在 `global.json` 中锁定
- **Docker** —— 运行集成测试（Testcontainers）
- **Git**

### 推荐工具

- **Visual Studio 2026** / **JetBrains Rider 2026.x** / **VS Code + C# Dev Kit**
- **Claude Code** —— 仓库 `.claude/skills/` 下已预置 Kun 专属技能包，可大幅提升 Claude 生成代码的合规度

### 初始构建

```bash
dotnet restore
dotnet build
dotnet test
```

## 🏗️ 分层原则（非常重要）

Kun Framework 严格遵守分层依赖，**任何 PR 违反这些规则都会被拒绝**：

```
Kun.Core
  └─ Kun.Mediator / Kun.Infrastructure / Kun.EventBus / Kun.Security / Kun.Validation
       └─ Kun.EntityFrameworkCore / Kun.Dapper / Kun.EventBus.RabbitMQ / Kun.Saga
            └─ Kun.AspNetCore / Kun.Grpc / Kun.Gateway
```

- **`Kun.Core` 零依赖**：不引用任何第三方包（连 EF Core、Dapper 都不行）
- **领域层零基础设施**：`AggregateRoot` 等基类绝不携带 ORM / JSON / 验证框架的任何属性
- **持久化映射下沉**：`IEntityTypeConfiguration<T>` 位于 `Kun.EntityFrameworkCore` 或业务项目的 Infrastructure 层

## 🎯 AOT 兼容性约定

每个模块的 csproj 会声明 `<IsAotCandidate>true</IsAotCandidate>`。**AOT 候选模块中**：

- ❌ 禁止使用运行时反射（`Activator.CreateInstance`、`Type.GetMethod`、`MakeGenericType` 等）
- ❌ 禁止在运行时生成 IL（`System.Reflection.Emit`）
- ✅ 必须使用 `JsonSerializerContext` 源生成而非默认 `JsonSerializer.Serialize`
- ✅ 必须使用 `[DapperAot]` 特性标注读侧查询方法
- ✅ CI 会对 AOT 候选服务执行 `dotnet publish -p:PublishAot=true`，零警告才允许合入

## 🤖 使用 Claude Skills

仓库根目录下 `.claude/skills/` 提供了 6 个 Kun 专属技能包。

### 使用 Claude Code

在仓库根目录运行 Claude Code，技能包自动加载。推荐在以下场景显式要求 Claude 参考对应技能：

| 任务 | 触发技能 |
| :--- | :--- |
| 新增聚合根、值对象、领域事件 | `kun-ddd-modeling` |
| 编写 Command / Query Handler | `kun-cqrs-mediator` |
| 发布集成事件 / 处理消费 | `kun-outbox-events` |
| 实现跨服务长事务 | `kun-saga` |
| 编写测试 | `kun-testing` |
| 任何涉及框架整体约定的工作 | `kun-framework-conventions` |

### 验证规范遵循

在提交 PR 前，建议在 Claude Code 中运行：

```
> 请根据 .claude/skills/kun-framework-conventions 检查我刚才的改动是否符合框架约定
```

## 📝 提交规范

本仓库使用 [Conventional Commits](https://www.conventionalcommits.org/)：

```
<type>(<scope>): <subject>

[optional body]

[optional footer]
```

**Type** 取值：

- `feat`: 新功能
- `fix`: Bug 修复
- `docs`: 文档变更
- `refactor`: 重构（非功能变更）
- `perf`: 性能优化
- `test`: 测试相关
- `build`: 构建系统 / 依赖变更
- `ci`: CI 配置变更
- `chore`: 其他杂项

**Scope** 通常是模块名去掉 `Kun.` 前缀：`mediator`、`saga`、`eventbus`、`security`、`gateway` 等。

**示例**：

```
feat(saga): add timeout detection background service

Implements SagaTimeoutDetector that scans SagaInstance table every 5s
and triggers compensation for instances where TimeoutAt < UtcNow.

Closes #42
```

## 🏷️ 发布与版本规范

Tag 与分支强绑定，**CI 会强制校验**（见 `.github/workflows/release.yml`）：

| 分支 | Tag 形式 | 语义 | 示例 |
| :--- | :--- | :--- | :--- |
| `main` | `vX.Y.Z` | 正式版本 | `v0.2.0` |
| `develop` | `vX.Y.Z-beta` | 预发布 | `v0.2.0-beta`、`v0.2.1-beta` |

规则：

- 从 `main` 打的 tag **一律不带后缀**；从 `develop` 打的 tag **一律加 `-beta`**。
- 其它后缀（`-alpha`、`-rc`）目前不在策略内，若打了会被 CI 标注为 `notice` 并跳过分支校验（但仍会走 NuGet 发布）。
- `develop → main` 合并后再从 `main` 打正式 tag；不要直接把 `-beta` 的 commit 重打成正式 tag。
- 打错分支会被 Release workflow 的 "Validate tag matches branch policy" 步骤拦下。

## 🔀 Pull Request 流程

1. **Fork 仓库**，从 `main` 切出特性分支：`feat/xxx` / `fix/xxx` / `docs/xxx`
2. **编写代码 + 测试**。任何新功能必须附带测试。
3. **本地验证**：
   ```bash
   dotnet build -c Release
   dotnet test
   # AOT 服务额外验证
   dotnet publish src/Kun.Gateway -c Release -p:PublishAot=true
   ```
4. **提交 PR**：填写 PR 模板，链接相关 Issue，说明影响范围与测试情况
5. **CI 全绿**：构建、测试、AOT 发布零警告、格式检查通过
6. **代码审查**：至少一位维护者 Approve 后方可合并

## 🧪 测试要求

参见 [kun-testing skill](.claude/skills/kun-testing/SKILL.md)。简而言之：

- **领域变更** → 领域单元测试（纯内存、无容器）
- **跨层变更**（仓储、Outbox、Saga）→ Testcontainers 集成测试
- **gRPC 服务变更** → 契约测试验证 Proto 不破坏兼容性

## 🐛 报告问题

提交 Issue 时请包含：

- 使用的 `.NET` 版本与 `Kun.*` NuGet 版本
- 最小可复现仓库 / 代码片段
- 预期行为与实际行为
- 如涉及运行时错误：完整堆栈与日志

## 📄 许可证

通过提交 PR，您同意将您的贡献以 [MIT 许可证](LICENSE) 发布。
