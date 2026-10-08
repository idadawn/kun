# 审计：同事务实体记录与 PostgreSQL 命令哈希链

实体审计入口为 `Kun.Auditing.AddKunAuditing<TContext>()`：在业务 `SavingChanges` 阶段把 `AuditLog` 和 Outbox 行登记到同一个 `KunDbContext`，由同一次保存提交。任一写入失败或业务事务回滚，两类审计记录都回滚；保存失败与取消会撤销暂存行，允许修正业务后重试。

## 实体审计与读取

```csharp
services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
services.AddKunDataConventions<AppDbContext>();
services.AddKunAuditing<AppDbContext>();
services.AddAuditable<Order>();
services.AddKunAudit().UseEntityFramework<AppDbContext>();
services.AddKunEventBus(registry => registry.RegisterAuditing());
```

宿主必须提供可信的 `ITenantContext`、`ICurrentUser`；若未提供则使用无租户、匿名上下文。`AddKunAuditing<TContext>()` 在宿主 `OnModelCreating` 前登记 `AuditLog`，使 `KunDbContext` 对它应用租户过滤。多租户宿主仍需装配 `AddKunTenantRls<TContext>()` 和数据库 RLS 策略。

- 每个被 `AddAuditable<TEntity>()` 声明的实体变更生成一行 `audit_logs` 与一行 Outbox，二者共享 Guid v7 标识。
- `kun.audit.event.v2` 使用字符串 `tenantId` / `userId`，从上下文填写；`AuditContext` 可覆盖 actor、来源 IP 与 trace，`ActorType` 支持 `user/service/agent/system`。
- 实体需要客户端已知的主键（推荐下面的 Guid v7 约定）；服务端自增键不能在 `SavingChanges` 阶段获得最终值。
- 不自动采集实体内容，避免把敏感字段写进审计载荷。实体审计写入失败会使业务保存失败；旧 `SwallowWriterExceptions` 选项不控制此入口。
- `IAuditReader.QueryAsync` 在数据库内按当前租户、主体、动作、资源与时间过滤，按时间、ID 倒序，限制每次 1–1,000 条。查询租户必须与当前上下文一致，不支持通过查询参数越权切换租户。

## 数据约定

模型配置最后调用 `modelBuilder.ApplyKunDataConventions()`，保存拦截器通过 `AddKunDataConventions<TContext>()` 自动装配：

- 表、列、键、索引使用 snake_case；未赋值的 Guid 主键生成 v7，显式主键保持不变。
- `IAuditedObject` 新增时写 `created_at/created_by`，修改时仅刷新 `updated_at/updated_by`，禁止改写创建字段。
- 主体支持 `string`、`Guid?`、`long?`、`int?`。匿名或系统操作写 null，不用空 Guid 或 0 冒充主体；无法转换的已认证主体拒绝保存。
- C# 无约束泛型的 `T?` 不会把值类型自动变成可空值类型。已有 `AggregateRoot<TKey>`（Guid 主体）需显式迁移为 `AggregateRoot<TKey, Guid?>`，并生成相应数据库迁移；旧公共基类保持兼容。
- 审计实体映射 shadow `KunRowVersion` 至 PostgreSQL `xmin`。冲突保留 `DbUpdateConcurrencyException`；需要 Result 边界时调用 `SaveChangesResultAsync()`，映射为 `data.concurrency_conflict` / `Conflict`，不自动覆盖获胜写入。

## 命令审计哈希链

```csharp
// NpgsqlDataSource 由宿主管理生命周期。
services.AddKunAudit(options => options.DefaultActorType = AuditActorType.Agent)
    .UsePostgresChain(dataSource, "audit");
services.AddKunMediator(builder => builder.AddAuditBehavior<CreateOrder, Result<Guid>>());
```

部署迁移阶段显式调用 `PostgresAuditChain.EnsureCreatedAsync()`；正常追加不建表。PostgreSQL 需 15+（测试为 16），使用 `UNIQUE NULLS NOT DISTINCT` 拦截相同前序，包括首条 null 前序。

`PostgresAuditChain` 同时实现 `IAuditChainState` 和 `IAuditSink`，必须使用同一个实例。每次追加在事务内创建或锁定该租户唯一链头（`FOR UPDATE`），读取最新 tip、插入记录并推进 tip 后提交。采用 READ COMMITTED 的语句快照，避免持续写入副本使等待方拿着旧的 SERIALIZABLE 快照反复重试；租户行锁提供完整的读改写串行化。`content_hash`、`(tenant_key, seq)` 和 `(tenant_key, prev_hash)` 唯一约束防止分叉。全局链与任意租户字符串使用不同键空间。

`WriteAsync()` 只接受当前追加作用域里租户、前序和内容哈希都匹配的一条记录；回调失败时记录与链头一起回滚。回调可能因数据库瞬时冲突重试，因此不得夹带事务外副作用。

`AuditEntry.ActorType` 默认 `unknown`，保持历史条目的哈希兼容；明确主体类型的新条目采用固定字段顺序的 JSON 编码后计算 SHA-256，避免分隔符歧义。用 `AuditContentHasher.Compute(entry)` 重放；变更 actor 类型也会改变哈希。数据库额外保存 UTC ticks，避免 PostgreSQL 微秒精度损失。

```csharp
string? previous = null;
await foreach (var row in chain.ReadAsync(tenantId, cancellationToken))
{
    if (row.PrevHash != previous || AuditContentHasher.Compute(row) != row.ContentHash)
        throw new InvalidOperationException("审计链断裂");
    previous = row.ContentHash;
}
```

读取哈希链是基础设施 API，调用方必须先授权。命令 Pipeline 延续既有行为：审计失败记录告警，不覆盖 handler 的返回值或原始异常。它不承诺与业务事务原子提交；需要业务与审计原子性的实体记录使用前述 `AddKunAuditing<TContext>()`。

## 旧入口迁移

- `Kun.Audit.EntityFrameworkCore.AuditSaveChangesInterceptor` 与 `AddKunAuditEntityFrameworkCore()` 是业务保存后再写审计的旧路径，已标记 `KUNAUDIT001` 废弃警告。
- `UseEventBus()`、`EventBusAuditWriter` 与 `kun.audit.record.v1` 路径已废弃；迁移到 `AddKunAuditing<TContext>()`，不要同时挂两个实体审计拦截器。
- 无泛型 `AddKunAuditing()` 与 `AddAuditingInterceptor()` 保留兼容旧 `kun.audit.event.v1`，新宿主采用泛型入口；v1 的 Guid 载荷不能承载任意字符串主体，不能把类型改动伪装为原 v1 事件。
- `RegisterAuditing()` 同时注册 v1/v2 稳定 ID，同时登记源生成的 JSON 类型信息；消费方应先同时支持两版，再切生产者，待历史消息排空后移除旧订阅。

## 验证

`TransactionalAuditTests` 验证业务／审计／Outbox 同提交与回滚、审计写入失败、失败后重试、创建字段保持、空主体、xmin 冲突与租户读取；`PostgresAuditChainTests` 用两个独立数据源并发追加 1,000 条，逐条重放验证无分叉，并覆盖追加后故障回滚及全局／租户隔离。
