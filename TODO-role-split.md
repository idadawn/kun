# Kun Framework 剩余任务 — 角色拆分跟踪表

> **临时跟踪文件**。基准来源：`TODO.md`、`docs/neuroagent-phase1-gap.md`、`.claude/skills/kun-*`。
> 已完成的原 TODO 条目不再罗列；仅列出 `[ ]` 未完成项与 Phase 1 Gap 补齐项。
> 完成后把 `- [ ]` 改为 `- [x]`。

**生成日期**：2026-04-24
**当前状态**：M1-M5 代码已完成；M6 代码完成，差运行时演练与发布仪式；neuroagent Phase 1 需新增 `Kun.Storage` + `Kun.Audit`

---

## 角色1（AI 执行）— 纯代码 / 文档 / 配置

### A. 阶段一遗留（Mediator / Validation SourceGen 回归）

> 状态：ADR fallback 延后。手工 `KunMediatorBuilder` 已等价工作。M5 后按需重评估。

- [ ] `src/Kun.Mediator.SourceGenerator/` 项目骨架
- [ ] SG：扫描 `IRequestHandler<,>` → 生成注册代码
- [ ] SG：为每个 Handler 生成专用 Wrapper（替换闭包工厂）
- [ ] `tests/Kun.Mediator.Tests/SourceGeneratorTests.cs`
- [ ] `src/Kun.Validation.SourceGenerator/` 项目
- [ ] `src/Kun.Validation/Attributes/` 属性包（`[Validate]` / `IValidatable`）
- [ ] SG：生成 `IValidator<T>` 实现
- [ ] SG：生成 `AddKunValidation` 扩展
- [ ] `tests/Kun.Validation.Tests/` 扩展用例覆盖 SG

### B. 阶段二遗留

- [ ] `Directory.Packages.props` 引入 `Respawn` 包
- [ ] 在 `PostgresFixture` 接入 Respawn，实现测试间快速清库

### C. 阶段四遗留

- [ ] Saga SourceGenerator：扫描 `ISagaStep<>` 实现并注册到 DI（Scoped 生命周期）

### D. 阶段六收尾（M6）

- [ ] `docs/observability/tracing-tour.md` 校对并补截图占位
- [ ] `CHANGELOG.md` 增加 `[1.0.0]` 节草稿

### E. Phase 1 新模块 — 阻塞 neuroagent（**最高优先级**）

#### E.1 `Kun.Storage` 新建（P1-KN-001）

- [ ] `src/Kun.Storage/Kun.Storage.csproj`（AOT 候选）
- [ ] 加入 `Directory.Build.props` 的 AOT 发布矩阵
- [ ] `Abstractions/IFileStorage.cs`（7 方法：`CreatePresignedUploadUrl` / `CreatePresignedDownloadUrl` / `PutObject` / `GetObject` / `HeadObject` / `DeleteObject` / `ListObjects`）
- [ ] `Abstractions/ObjectMetadata.cs`
- [ ] `Abstractions/PresignedUploadUrl.cs`
- [ ] `Abstractions/PresignedDownloadUrl.cs`
- [ ] `Implementations/S3FileStorage.cs`（AWS SDK for .NET，兼容 MinIO）
- [ ] 实现活跃 web MIME 强制 `Content-Disposition: attachment` 逻辑
- [ ] `Implementations/LocalFileSystemStorage.cs`
- [ ] `DependencyInjection/KunStorageServiceCollectionExtensions.cs`
- [ ] `tests/Kun.Storage.Tests/Kun.Storage.Tests.csproj`
- [ ] 单元测试：接口语义、presigned URL 生成、Content-Disposition 强制、本地实现路径遍历防护
- [ ] 集成测试：MinIO Testcontainer 全链路
- [ ] 注册到 `Kun.sln`
- [ ] 更新 `Directory.Packages.props`（AWSSDK.S3 等）
- [ ] 更新 `README.md` 模块矩阵
- [ ] 更新 `docs/architecture/module-map.md`
- [ ] 更新 `docs/neuroagent-phase1-gap.md`（P1-KN-001 标记完成）
- [ ] `CHANGELOG.md` 记录

#### E.2 S2S JWT claims 对齐（P1-KN-002，微调）

- [ ] 核对 SAD v3.0 §12 要求的 claims：`sub`、`tenant_id`、`roles`、`trace_id`、`membership_id`、15min exp
- [ ] 按差异调整 `S2sTokenOptions`
- [ ] 按差异调整 `S2sTokenIssuer`（Kun.Gateway）
- [ ] 按差异调整 `JwtCurrentUser`、`JwtTenantContext`、`JwtTracedCurrentUser`（Kun.Security）
- [ ] `tests/Kun.Security.Tests/` 补对应断言
- [ ] 更新 `docs/security/s2s-jwt.md`

#### E.3 `IEventTypeRegistry` 属性驱动注册（P1-KN-003，小幅增强）

- [ ] 新增 `[IntegrationEventId("xxx.v1")]` 属性
- [ ] `EventTypeRegistry` 支持属性扫描注册路径
- [ ] 保持 AOT 安全（不用运行时反射，走 SG 或 `IEventTypeRegistrar` 显式注册）
- [ ] 追加单测
- [ ] 在 `samples/Kun.Sample.Catalog.Contracts` 中替换为属性写法作为样例

#### E.4 RLS 第二道防线（P1-CS-011 加固，可选但推荐）

- [ ] 在 `KunDbContext` 新增 connection interceptor
- [ ] 每次打开连接执行 `SET LOCAL app.current_tenant_id = '<tenant>'`
- [ ] 集成测试覆盖租户隔离的 RLS 二次验证

### F. Phase 1 新模块 — M4 Week 7 之后

#### F.1 `Kun.Audit` 新建（P1-CS-019）

- [ ] `src/Kun.Audit/Kun.Audit.csproj`
- [ ] `AuditLogPipelineBehavior<TRequest, TResponse>`：捕获 Command（actor、tenant、command name、result、延迟）
- [ ] 哈希链字段：`prev_hash` + `content_hash`（对齐 SAD §15.5、§6.7 audit schema）
- [ ] `IAuditSink` 抽象
- [ ] `EfAuditSink` 实现（异步写出，`audit.operations` 表）
- [ ] `DependencyInjection/KunAuditServiceCollectionExtensions.cs`
- [ ] 接入 `KunMediatorBuilder`
- [ ] `tests/Kun.Audit.Tests/`：哈希链连续性、异步写出、失败隔离
- [ ] 注册到 sln、Packages、README、module-map
- [ ] `CHANGELOG.md` 记录

### G. 文档/约定小项

- [ ] 阶段二/三/四的 `CHANGELOG.md` 对应节完整性检查（缺则补）
- [ ] `docs/neuroagent-phase1-gap.md` 在 Kun.Storage / Kun.Audit 落地后更新"需新建"章节结论

---

## 角色2（人工执行）— 环境 / 仪式 / 外部依赖

### H. AOT 本地/CI 发布验证（需 C++ linker）

- [ ] 搭好带 native linker 的构建环境（推荐 Linux）
- [ ] `Kun.Mediator` — AOT 零警告（阶段一验收）
- [ ] `Kun.Validation` — AOT 零警告（阶段一验收）
- [ ] `Kun.EventBus` — AOT 零警告（阶段二验收）
- [ ] `Kun.Dapper` — AOT 零警告（阶段二验收）
- [ ] `Kun.EventBus.RabbitMQ` — AOT 零警告（阶段三验收）
- [ ] `Kun.Saga` — AOT 零警告（阶段四验收）
- [ ] `Kun.AspNetCore` — AOT 零警告（阶段五验收）
- [ ] `Kun.Grpc` — AOT 零警告（阶段五验收）
- [ ] `Kun.Gateway` — AOT 零警告（阶段五验收）
- [ ] `Kun.Security` — AOT 零警告（阶段五验收）
- [ ] `samples/Kun.Sample.AppHost` 下所有 AOT 候选子项目（阶段六验收）
- [ ] `Kun.Storage` AOT 零警告（角色1落地后）
- [ ] `Kun.Audit` AOT 零警告（角色1落地后）
- [ ] 实测 `Kun.Sample.Minimal` AOT 启动时间 → 回填 `docs/benchmarks/aot-startup.md`

### I. 运行时人工演练（需 Docker 常驻 + 真实服务）

- [ ] **阶段三**：本地两服务发/收事件，Aspire Dashboard 确认链路完整
- [ ] **阶段四**：`SELECT * FROM saga_instances` 肉眼验证 Running / Completed / Compensated 三态
- [ ] **阶段六**：`dotnet run --project samples/Kun.Sample.AppHost` 一键启动全家桶
- [ ] **阶段六**：`POST /demo/happy-path` curl 复现
- [ ] **阶段六**：`POST /demo/trigger-payment-failure` curl 复现
- [ ] **阶段六**：`POST /demo/trigger-timeout` curl 复现
- [ ] **阶段六**：Aspire Dashboard 确认完整 TraceId 串联（HTTP → CQRS → EF → Outbox → RabbitMQ → 消费端 → Saga 步骤）
- [ ] **阶段六**：Saga 补偿链路通过 `saga_id` 关联正/反向 Trace
- [ ] **阶段六**：找一位无 Kun 经验同事按 `docs/getting-started.md` 走一遍，记录卡点

### J. GitHub 仓库/流程权限

- [ ] 保护 `main` 分支（PR 审查 + CI 全绿强制）
- [ ] 新建 `develop` 分支作为开发主干
- [ ] 策略：`main` 仅接受来自 `develop` 的合并

### K. 发布仪式（需 NuGet feed / tag 权限）

- [ ] `0.1.0-alpha.1`（阶段一）— NuGet 发布 + Git tag
- [ ] `0.2.0-alpha.1`（阶段二）— NuGet 发布 + Git tag
- [ ] `0.3.0-alpha.1`（阶段三）— NuGet 发布 + Git tag
- [ ] `0.5.0-beta.1`（阶段四）— NuGet 发布 + Git tag
- [ ] `1.0.0-rc.1`（阶段五）— NuGet 发布 + Git tag
- [ ] `1.0.0`（阶段六）— 正式版发布 + Git tag + 决定 NuGet feed
- [ ] 流程合规：按 `CONTRIBUTING.md` §发布与版本规范，`develop → main` 合并后从 `main` 打稳定 tag；不要在 `-beta` commit 上重 tag

### L. 可选交付

- [ ] 阶段六：录制 3 分钟演示视频（启动 Aspire → 下单 → 触发失败 → 观察补偿）

---

## 执行顺序建议

### 解除 neuroagent Phase 1 阻塞（角色1，立即）
1. `Kun.Storage` 新建（E.1）— **最高优先级，唯一硬阻塞**
2. S2S JWT claims 对齐（E.2）
3. `IEventTypeRegistry` 属性驱动注册（E.3）
4. `KunDbContext` RLS interceptor（E.4）

### 中期（角色1，M4 Week 7 起）
5. `Kun.Audit` 新建（F.1）
6. Mediator / Validation / Saga SourceGenerator 回归（A / C）
7. Respawn 接入（B）

### 环境/仪式（角色2，与角色1并行）
8. AOT 发布验证矩阵（H）
9. 阶段三~六运行时演练（I）
10. 发布仪式 M1→M6（K）
11. GitHub 分支保护（J）

---

## 状态快照（人工维护）

| 模块 / 任务 | 负责人 | 状态 | 备注 |
|---|---|---|---|
| Kun.Storage | 角色1 | ✅ 已完成 2026-04-24 | 7 方法 + ContentDispositionPolicy + Local HMAC stub + MinIO 集成测试；本地无 dotnet，构建/测试待 CI |
| S2S JWT claims 对齐 | 角色1 | ✅ 已完成 2026-04-24 | Set B (`Kun.Security.S2SJwt`) —— `S2SJwtIssueRequest` 记录型参数，emit roles/membership_id/trace_id，TTL 默认 900s，修顺带 fix 了多 aud 写到 JWT header 的 bug；Set A (Kun.Gateway 60s) 刻意未动 |
| IEventTypeRegistry 属性驱动 | 角色1 | ✅ 已完成 2026-04-24 | `[IntegrationEventId("event.v1")]` + `Register<T>()` 无参重载（标 RequiresUnreferencedCode），显式字符串路径保留；3 个样例事件已迁移 |
| RLS interceptor | 角色1 | ✅ 已完成 2026-04-24 | `TenantRlsConnectionInterceptor`（opt-in）：SET/RESET `app.current_tenant_id`；白名单防注入；只对 Npgsql 激活；policy 模板 + 3 条 Postgres 集成测试 + `docs/security/multi-tenancy-rls.md` |
| Kun.Audit | 角色1 | 未开始 | M4 Week 7 起 |
| Mediator SG 回归 | 角色1 | 延后 | ADR fallback |
| Validation SG 回归 | 角色1 | 延后 | ADR fallback |
| Saga SG | 角色1 | 延后 | |
| Respawn 接入 | 角色1 | 未开始 | |
| AOT 发布矩阵 | 角色2 | 未开始 | 需 linker |
| 运行时演练（三/四/六） | 角色2 | 未开始 | 需 Docker |
| GitHub 分支保护 | 角色2 | 未开始 | |
| NuGet 发布 M1→M6 | 角色2 | 未开始 | 需 feed 凭据 |
| 演示视频 | 角色2 | 可选 | |
