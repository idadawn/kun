# Kun Framework 路线图

> 精简的公开路线图。只保留**未完成**条目与里程碑，已交付项在 `CHANGELOG.md` 与 git 历史中追踪。
> 最近更新：2026-06-12

## 当前状态

M1–M6 代码全部完成，Sprint 1 + Sprint 2 gap-closure 已全部合入主干（仓库自 2026-06-11 起 trunk-based，`develop` 已删除，`main` 为唯一长期分支）：

| 缺口 | 内容 | 落地 commit |
|---|---|---|
| P0-1 | `IFileStorage` + Local + S3 实现 + 样例 | `066c944` |
| P0-2 | `ITracedCurrentUser` + `trace_id` claim | `4564278` / `4f5aa62` |
| P0-3 | `UseKunSchema` + `SchemaConsistencyInterceptor` + per-service schema migrations | `ce55f5c` / `deb3d11` / `c8ca297` |
| P0-4 | `IOutboxWriter` primitive | `e25be27` / `2de6cd2` |
| P1-1 | `IEventTypeRegistry.GetAll()` + `tools/Kun.IntegrationEvents.SchemaGen` | `bf55161` |
| P1-3 | Internal JWKS endpoint (gated) | `82d53f8` |
| P1-4 | `TenantRlsTransactionInterceptor` + PgBouncer M2 测试 | `fa40945` / `6b7c9a1` |
| P1-5 | PgBouncer README + 100-并发压测 | `aa52801` |
| P1-6 | `IS2sPrincipalResolver` hook + overloaded ctor (3-arg obsolete) | `29870ed` |
| P1-7 | OpenAPI pipeline template + Catalog 样例 | `f3312e2` |
| P1-8 | `docs/schema-per-service.md` + 样例 README 联动 | `bd896b4` |

全量 `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 零错误（预存在 8 条 Aspire `CS1591` 为已知噪声）；所有相关单元测试绿，Docker 依赖的集成测试在 CI 跑。

**2026-06-12 快照**：`Kun.sln` 补齐全部 32 个 src + 29 个 tests 项目（#24）与 benchmarks（#25），CI 构建/测试门禁首次覆盖全仓；两处高频 CI flaky（LogBaggage 监听器竞态 #27、PgBouncer 启动/风暴竞态 #28/#29）已根治；首个 CalVer 正式版 **`v2026.6.12`** 从 `main` 打 tag，经 Release 工作流将全部可打包模块推送 NuGet.org + GitHub Packages。

## 里程碑

> **2026-06-11 起版本号策略切换为 CalVer**（`vYYYY.M.D[-N|-beta.N]`，于 `main` 打 tag，与 idadawn/iclaw 一致，详见 `CONTRIBUTING.md`）。下表的 SemVer 预览版号为历史规划名义，不再对应实际 tag；"达成标志"列仍有效。

| 预览版号（历史名义） | 达成标志 | 状态 |
|:---|:---|:---:|
| `0.1.0-alpha.1` | CQRS 调度 + 验证 AOT 跑通 | 代码就绪，待发版仪式 |
| `0.2.0-alpha.1` | 持久化双引擎 + Outbox 原子性 | 代码就绪，待发版仪式 |
| `0.3.0-alpha.1` | 消息总线端到端打通 | 代码就绪，待发版仪式 |
| `0.5.0-beta.1` | 核心能力齐备，进入 beta | 代码就绪，待发版仪式 |
| `1.0.0-rc.1` | API 冻结，进入 RC | 待 Sprint 2 完成 |
| `1.0.0` | 正式发布 | 待 RC 走完 |

发版仪式已于 **2026-06-12** 以首个 CalVer tag `v2026.6.12` 执行（NuGet.org + GitHub Packages + GitHub Release），上表 SemVer 里程碑的"达成标志"全部满足、随该版一次性交付。后续发版按 `docs/release-checklist.md` 走：更新 `CHANGELOG.md` → main 打 CalVer tag → `release.yml` 自动完成校验/打包/推送。

## 剩余工作

### Phase 2 候选清单

白皮书 §12.2 的 5 条 Phase-2 路标已在 [`docs/roadmap-p2.md`](roadmap-p2.md) 展开为可立项的 kickoff 清单（作用域、可交付物、前置依赖、风险、工作量、PRD 草稿），用户按图索骥挑选首个赛道后再进入 `ralplan`。

### Sprint 2 候选项（`.omc/plans/kun-gap-closure-v1.md` 内部计划区）

- ~~**P1-9** NuGet 首次发版~~——已由 `v2026.6.12`（CalVer）完成，2026-06-12

### Code-review MAJOR follow-ups 状态

全部已消化（合入 develop 或留为文档化 follow-up）：

| # | 内容 | Commit |
|---|---|---|
| 1 | JWKS 响应 mtime 缓存 | `922d922` |
| 2 | `S2sTokenIssuer.IssueForAsync` | `084c29a` |
| 3 | `LocalFileSystemStorage` 流式 MaxBytes | `6d47b5a` |
| 4 | S3 presigned-POST MaxBytes 签名 | `06ddf61` |
| 6 | `ObjectMetadata.ContentType` nullable | `f7087d3` |
| 7 | JWKS `additionalGate` | `2a3c544` |
| 8 | `SchemaConsistencyInterceptor` 读路径 | 按 M5 刻意设计，不改 |
| M2 | `IntegrationEventOutboxAccumulator` JsonTypeInfo pathway | `d4a0f49` |

### 环境与流程类（ADR 延后 / 需外部条件）

- **SourceGenerator 族**：`Kun.Mediator.SourceGenerator` / `Kun.Validation.SourceGenerator` / Saga 步骤注册。当前用手工 `KunMediatorBuilder` + `AddValidator<>` + `AddSaga<>` 等价替代；AOT 安全。等 M5 后重评。
- **Respawn 包**：集成测试快速清库包，未引入（需权衡 dependency surface）。
- **AOT 发布本地验证**：本机缺 MSVC C++ linker；CI 的 `ubuntu-latest` 上 `dotnet publish -p:PublishAot=true` 矩阵已覆盖 `Kun.Gateway / Mediator / Core / Grpc / Validation / Storage.Abstractions / Storage.Local / EventBus`，`Kun.Storage.S3` 按 A6 刻意排除。
- **链路追踪手工走查**：需启动 Aspire AppHost 并人工触发 Saga Happy / Failure / Timeout 三条路径，观察 Dashboard 中的单一 TraceId 串联；写入 `docs/observability/tracing-tour.md` 时的证据材料。
- **演示视频**：3 分钟 Aspire 启动 + 补偿演示；可选。
- **GitHub 分支保护**：`main` 分支要求 PR 审查 + CI 全绿，用户在仓库 Settings 中配置。
- **AOT 启动基准数据回填**：`docs/benchmarks/aot-startup.md` 骨架已建，待实测 `Kun.Sample.Minimal` AOT 启动时间后回填数字。
- **getting-started 外部走查**：请一位无 Kun 经验的开发者按 `docs/getting-started.md` 完整走一遍，记录并修复卡点。

> 以上条目并入自原根目录 `TODO-remaining.md`（2026-06-12 删除）；其中 CI 矩阵验证、AOT publish 矩阵、NuGet 发版仪式等已完成项不再保留。

## Per-PR 检查清单

每个 PR 合入前必须确认：

- [ ] `dotnet format --verify-no-changes` 通过
- [ ] `MSYS_NO_PATHCONV=1 dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0 errors（Windows/MSYS 下必须带 `MSYS_NO_PATHCONV=1`，否则 `/warnaserror` 会被路径转换破坏）
- [ ] `dotnet test` 全绿（Docker 依赖的集成测试会 `Assert.Skip`，算通过）
- [ ] 涉及 AOT 候选模块时，相关 `dotnet publish ... -p:PublishAot=true` 零警告（CI 矩阵负责）
- [ ] 公开 API 变更同步更新 `CHANGELOG.md` 的 `[Unreleased]` 节
- [ ] 公开 API 变更同步 `PublicAPI.Unshipped.txt`
- [ ] 提交信息符合 Conventional Commits（scope = 不带 `Kun.` 前缀的模块名）

## 发布前最终验收

触发 `1.0.0` 正式发版前须全部过：

- [ ] `dotnet run --project samples/Kun.Sample.AppHost` 一键起全部服务，Aspire Dashboard 可见
- [ ] Demo 三条路径（正常 / 失败补偿 / 超时补偿）均可通过 curl 或 Postman 复现
- [ ] Aspire Dashboard 中可看到完整 TraceId 跨服务串联
- [ ] 全部 AOT 候选模块在 CI 矩阵中 `dotnet publish -p:PublishAot=true` 零警告
- [ ] `docs/getting-started.md` 被无 Kun 经验的同事测试走通
- [ ] `CHANGELOG.md` 新增 `[1.0.0]` 节
- [ ] NuGet 包已推到 feed，Git tag `v1.0.0` 已打

## 任务跟踪的物理位置

- **本文件** (`docs/roadmap.md`)：公开路线图，面向贡献者。
- **`.omc/plans/*.md`**：内部 sprint 计划与 gap 清单，不入 git（由 `.gitignore` 排除），是 Claude Code 会话的工作区。
- **`CHANGELOG.md`**：按版本记录已发布变更。
- **Conventional Commits**：每条 commit 本身就是最细粒度的任务追踪，`git log` 即可还原交付时序。

原根目录的 `TODO.md` 已合并进本文件（2026-04-22）。
