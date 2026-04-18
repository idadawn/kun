# Pull Request

## 变更概述

简要说明此 PR 的目的与主要改动。

## 变更类型

- [ ] 🚀 新功能 (`feat`)
- [ ] 🐛 Bug 修复 (`fix`)
- [ ] 📚 文档 (`docs`)
- [ ] ♻️ 重构 (`refactor`)
- [ ] ⚡ 性能优化 (`perf`)
- [ ] ✅ 测试 (`test`)
- [ ] 🏗️ 构建 / CI (`build`/`ci`)
- [ ] 🧹 杂项 (`chore`)

## 相关 Issue

Closes #

## 自检清单

- [ ] 本地 `dotnet build -c Release` 通过
- [ ] 本地 `dotnet test` 全绿
- [ ] 若修改了 AOT 候选模块，本地 `dotnet publish -p:PublishAot=true` 零警告
- [ ] 遵循 `.claude/skills/kun-framework-conventions` 的分层与命名规范
- [ ] 新功能附带相应层级的测试（参考 `.claude/skills/kun-testing`）
- [ ] 若引入跨服务事件，稳定事件 ID 已注册到 `IEventTypeRegistry`
- [ ] 公开 API 有 XML 文档注释
- [ ] 提交信息遵循 Conventional Commits

## 测试说明

（描述你如何验证此 PR；截图、终端输出等）

## Breaking Changes

- [ ] 本 PR 无破坏性变更
- [ ] 本 PR 包含破坏性变更（请在下方说明迁移路径）

## 其他信息
