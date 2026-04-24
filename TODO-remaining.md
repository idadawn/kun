# Kun Framework 剩余工作清单

> **基准**：2026-04-24，`main` 分支 `bbdced2`，23 个 commit 已推 `origin/main`，Phase 1 gap 全量关闭。
> **来源合并**：`TODO.md` 中所有 `[ ]` 未完成项 + `TODO-role-split.md` 剩余项 + `TODO.md` §阶段验收清单。
> **分期规则**：按"**立刻能做 → 要验证/外部依赖 → 可延后/可选**"三段，不再区分角色。

---

## 📊 完成度快照

| 维度 | 状态 |
|---|---|
| **Phase 1 gap（neuroagent 就绪）** | ✅ 100%（5/5 关闭） |
| **M1–M6 代码交付** | ✅ 全部落地（含新增 `Kun.Storage`、`Kun.Audit` 共 15 个模块） |
| **阶段验收（测试全绿 + AOT publish）** | ⏳ 待 CI / 师父本机验证 |
| **运行时演练（Aspire / RabbitMQ / Saga）** | ⏳ 未开始 |
| **NuGet 发布仪式（M1 → M6 共 6 版本）** | ⏳ 未开始 |
| **SourceGenerator 回归（Mediator / Validation / Saga）** | 📋 ADR fallback 延后 |

---

## 🥇 P1 — 立刻可做（无外部依赖，纯代码 + 文档）

**预计总工作量：1–2 小时。**

### P1.1 测试基础设施：Respawn 接入

- [ ] `Directory.Packages.props` 增加 `Respawn` 包
- [ ] `tests/Kun.IntegrationTests/Fixtures/PostgresFixture.cs` 接入 Respawn 实现测试间快速清库
- **价值**：每个集成测试类不再需要拆容器/重建库，显著降低 CI 时长

### P1.2 M6 文档收尾

- [ ] `docs/observability/tracing-tour.md` 校对 + 截图占位（待师父运行时演练完补真实截图）
- [ ] `CHANGELOG.md` 增加 `[1.0.0]` 节草稿
- [ ] 阶段二 / 三 / 四 `CHANGELOG` 对应节完整性核对（补漏即可）

### P1.3 运行时演练前置文档

- [ ] `docs/benchmarks/aot-startup.md` 新建空骨架（待 AOT publish 矩阵跑完后回填数字）
- [ ] `docs/observability/tracing-tour.md` 把"HTTP → CQRS → EF → Outbox → RabbitMQ → 消费端 → Saga 步骤"完整链路用文字画出来（截图待补）

---

## 🥈 P2 — 验证与发布（需 CI / Docker / GitHub / NuGet 凭据）

**前置**：P1 完成 + 当前 `origin/main` 的 CI 全绿。

### P2.1 构建与测试验证（CI 触发）

- [ ] GitHub Actions `.github/workflows/ci.yml` 全矩阵通过：
  - [ ] `dotnet build -c Release`
  - [ ] `dotnet test -c Release`（含 Kun.Audit.Tests 21 条、Kun.Storage.Tests、集成测试）
  - [ ] `dotnet format --verify-no-changes`
  - [ ] `scripts/check-layering.sh`（Kun.Core 零第三方）
  - [ ] PublicAPI Unshipped 签名校验

### P2.2 AOT publish 矩阵（需 C++ linker 的 Linux/macOS 构建环境）

对每个 AOT 候选模块跑 `dotnet publish -c Release -p:PublishAot=true /warnaserror`，零警告才算过：

- [ ] `Kun.Core`
- [ ] `Kun.Mediator`
- [ ] `Kun.Validation`
- [ ] `Kun.EventBus`
- [ ] `Kun.EventBus.RabbitMQ`
- [ ] `Kun.Dapper`
- [ ] `Kun.Saga`
- [ ] `Kun.Security`
- [ ] `Kun.AspNetCore`
- [ ] `Kun.Grpc`
- [ ] `Kun.Gateway`
- [ ] `Kun.Audit`（本次 session 新增）
- [ ] `samples/Kun.Sample.AppHost` 下所有 AOT 候选子项目

**注**：`Kun.EntityFrameworkCore` 与 `Kun.Storage` 是 trim-safe-only，AOT 非强制。

### P2.3 运行时手工演练（需 Docker 常驻）

- [ ] `dotnet run --project samples/Kun.Sample.AppHost` 一键启动 Aspire 全家桶
- [ ] `POST /demo/happy-path` curl 复现 Saga 正向成功
- [ ] `POST /demo/trigger-payment-failure` curl 复现补偿路径
- [ ] `POST /demo/trigger-timeout` curl 复现超时路径
- [ ] Aspire Dashboard 确认完整 TraceId 串联（HTTP → CQRS → EF → Outbox → RabbitMQ → 消费端 → Saga 步骤同一 TraceId）
- [ ] Saga 补偿链路通过 `saga_id` 关联正 / 反向 Trace
- [ ] `SELECT * FROM saga_instances` 肉眼验证 Running / Completed / Compensated 三态
- [ ] 阶段三：两本地服务发 / 收事件，Aspire Dashboard 链路完整
- [ ] 回填 `docs/observability/tracing-tour.md` 真实截图

### P2.4 GitHub 仓库治理

- [ ] `main` 分支保护：PR 审查 + CI 全绿强制
- [ ] 新建 `develop` 分支作为开发主干
- [ ] 策略固化：`main` 仅接受来自 `develop` 的合并

### P2.5 NuGet 发布仪式

按 `CONTRIBUTING.md` §发布与版本规范，`develop → main` 合并后再从 `main` 打稳定 tag（不要在 `-beta` commit 上重 tag）：

- [ ] `0.1.0-alpha.1`（M1：Core + Mediator + Validation）
- [ ] `0.2.0-alpha.1`（M2：EF 写侧 + Dapper 读侧 + Outbox 原子性）
- [ ] `0.3.0-alpha.1`（M3：RabbitMQ + Outbox 发布器 + 消费幂等）
- [ ] `0.5.0-beta.1`（M4：Saga 编排器 + 步骤执行 + 超时检测）
- [ ] `1.0.0-rc.1`（M5：ASP.NET Core + gRPC + Gateway + S2S JWT）
- [ ] `1.0.0`（M6：样例 + OpenTelemetry + 文档）🎉
- [ ] NuGet feed 选定（nuget.org 公共 feed 或内部私有 feed）

### P2.6 上线体验校验

- [ ] 找一位无 Kun 经验同事按 `docs/getting-started.md` 走一遍，记录卡点
- [ ] 修复卡点 → 回炉 getting-started

---

## 🥉 P3 — 延后项与可选（ADR fallback / 非阻塞）

### P3.1 Mediator SourceGenerator 回归

> 状态：ADR fallback 延后。当前手工 `KunMediatorBuilder` 已等价工作，AOT 安全零反射。

- [ ] `src/Kun.Mediator.SourceGenerator/` 项目骨架
- [ ] SG 扫描 `IRequestHandler<,>` → 生成注册代码
- [ ] SG 为每个 Handler 生成专用 Wrapper（替换闭包工厂）
- [ ] `tests/Kun.Mediator.Tests/SourceGeneratorTests.cs`
- [ ] 保留现有 `KunMediatorBuilder` 作为后备 API

### P3.2 Validation SourceGenerator + 属性包回归

> 状态：与 Mediator SG 同步延后。无 SG 时属性无运行时意义。

- [ ] `src/Kun.Validation/Attributes/`（`[Validate]` / `IValidatable`）
- [ ] `src/Kun.Validation.SourceGenerator/` 项目
- [ ] SG 扫描 `[Validate]` / `IValidatable` → 生成 `IValidator<T>` 实现
- [ ] SG 生成 `AddKunValidation` 扩展
- [ ] `tests/Kun.Validation.Tests/` 扩展覆盖 SG

### P3.3 Saga SourceGenerator

- [ ] SG 扫描 `ISagaStep<>` 实现并注册到 DI（Scoped 生命周期）
- [ ] 对应测试

### P3.4 性能 Benchmark

- [ ] 实测 `Kun.Sample.Minimal` AOT 启动时间 → 回填 `docs/benchmarks/aot-startup.md`
- [ ] Kun.Mediator AOT 调度耗时 baseline（可选）

### P3.5 可选交付

- [ ] 录制 3 分钟演示视频（启动 Aspire → 下单 → 触发失败 → 观察补偿）
- [ ] Result-aware Validation Behavior（返回 `Result.Fail(Error.Validation(...))` 代替抛异常，可选 follow-up）
- [ ] `AddKunDapper` API rework（`ReadDbOptions.ConnectionString` 的 required init 当前 API 略显冗余）
- [ ] Gateway 可观测性（等 OTel 包达到 AOT parity 后追加）

---

## 📈 里程碑与建议节奏

| 里程碑 | 预期触发 | 内容 |
|---|---|---|
| **M1 Ready** | P1 完成 + 当前 CI 全绿 | 具备 "本地开发者一次 checkout 即可跑通所有单元/集成测试" 的能力 |
| **M2 Validated** | P2.1 + P2.2 + P2.3 完成 | 每个 AOT 模块 publish 零警告 + 运行时三条 Saga 路径在 Aspire Dashboard 可视化复现 |
| **M3 Released** | P2.4 + P2.5 + P2.6 完成 | 稳定 `1.0.0` 落 NuGet feed + getting-started 被外人走通 + 分支保护开启 |
| **M4 Evolved**（可选） | P3.x 逐步合入 | SG 回归，API 进一步精简 |

---

## ⚠️ 执行优先级逻辑

1. **P1 先做**：纯代码/文档，不依赖师父机器或 CI；做完 Kun 的"测试跑起来更快 + 文档更齐"两个小改善
2. **P2 待外部就位**：师父跑 CI 看红绿，搭好带 linker 的 Linux 环境跑 AOT 矩阵，准备 Docker 演练，配好 NuGet 凭据 —— 这块我无力独立推进
3. **P3 按需**：SG 回归是大项目，除非师父明确要启动；benchmark / 视频 / follow-up API 调整都可随时插队

若师父同意节奏，**我下一步就开干 P1.1 Respawn 接入**。待 CI 结果出来后，如有红灯，我追修优先级高于一切其他。
