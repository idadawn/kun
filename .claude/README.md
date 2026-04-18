# Kun Framework · Claude Skills

此目录包含 **6 个专为 Kun Framework 定制的 Claude Skills**。在 Claude Code 中打开本仓库时，这些 Skills 会被自动加载，确保 Claude 生成的代码严格遵循框架约定。

## Skills 列表

| 编号 | Skill | 何时触发 |
| :---: | :--- | :--- |
| 1 | [**kun-framework-conventions**](skills/kun-framework-conventions/SKILL.md) | 总纲。任何 Kun 代码库内的编码工作——分层、AOT、命名、csproj 配置 |
| 2 | [**kun-ddd-modeling**](skills/kun-ddd-modeling/SKILL.md) | 聚合根、值对象、领域事件、规约模式 |
| 3 | [**kun-cqrs-mediator**](skills/kun-cqrs-mediator/SKILL.md) | Command / Query / Handler / Pipeline Behavior |
| 4 | [**kun-outbox-events**](skills/kun-outbox-events/SKILL.md) | 集成事件发布、消费幂等、事件版本演进 |
| 5 | [**kun-saga**](skills/kun-saga/SKILL.md) | Saga 编排器、步骤、补偿、超时 |
| 6 | [**kun-testing**](skills/kun-testing/SKILL.md) | 单元 / 集成 / 契约 / AOT 四层测试 |

## 阅读顺序

`kun-framework-conventions` 是**所有其他 Skills 的共同前置**。具体任务时：

- 写聚合根 → `conventions` + `ddd-modeling`
- 写 Command Handler → `conventions` + `cqrs-mediator`
- 发集成事件 → `conventions` + `outbox-events`
- 做跨服务流程 → `conventions` + `cqrs-mediator` + `outbox-events` + `saga`
- 编写测试 → `conventions` + `testing`（+ 对应业务 skill 以理解被测对象）

## 在 Claude Code 中使用

打开 Claude Code 进入本仓库，输入任何涉及 Kun 代码的任务，Claude 会自动检索相关 Skill。若希望显式触发，可以说：

```
> 请遵循 .claude/skills/kun-ddd-modeling 为订单服务实现 Order 聚合根
```

## 在 PR 前自验

```
> 请根据 .claude/skills/kun-framework-conventions 检查我刚才的改动是否符合框架约定
```

## 维护

当框架的核心设计发生演进（新增模块、更换关键选型、调整分层），Skills 必须同步更新。遵循以下原则：

- Skill 的 `description` 字段是主要触发依据，要"推着"Claude 用（`kun-framework-conventions` 里已示范）
- 正文保持 < 500 行
- 代码示例优先于文字描述
- 反模式（❌）和正模式（✅）并列，帮助 Claude 识别边界
