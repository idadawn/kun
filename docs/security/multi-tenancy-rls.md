# Multi-Tenancy: Row-Level Security as Defence in Depth

Kun enforces tenant isolation at two independent layers:

1. **Application layer** — `KunDbContext` applies a global query filter to every
   entity implementing `IMultiTenant`, scoping reads by `ITenantContext.TenantId`.
   This is implemented inside the EF Core query pipeline.
2. **Database layer** — PostgreSQL Row-Level Security (RLS) policies defined on
   each tenant-scoped table, driven by the session variable
   `app.current_tenant_id` that Kun pushes in on every connection open.

Either layer alone is enough for the happy path; both together give a
belt-and-braces posture: a bug in the ORM filter does not silently leak rows,
and a bug in the RLS policy is caught by the ORM filter. This document covers
the second layer.

---

## The TenantRlsConnectionInterceptor

`src/Kun.EntityFrameworkCore/Interceptors/TenantRlsConnectionInterceptor.cs`

The interceptor hooks EF Core's `DbConnectionInterceptor` events:

- `ConnectionOpenedAsync` → emits `SET app.current_tenant_id = '<tenant>'`
  when `ITenantContext.TenantId` is non-empty; `RESET app.current_tenant_id`
  otherwise.
- `ConnectionClosingAsync` → emits `RESET app.current_tenant_id` so pooled
  connections never leak the previous request's tenant scope.

Only activates when the underlying `DbConnection` is `Npgsql.NpgsqlConnection`
(namespace-prefix check on the type's FQN). Other providers are silently
no-ops so this module does not need to take a direct Npgsql dependency.

### SQL-injection guard

`SET` statements cannot be parameterised, so the tenant id is inlined into the
command text. The interceptor refuses to emit the command unless the value
matches the strict whitelist:

```
^[A-Za-z0-9._-]{1,128}$
```

Exposed as `TenantRlsConnectionInterceptor.SafeTenantIdPattern`. A tenant id
that does not match (spaces, quotes, semicolons, etc.) throws
`InvalidOperationException` rather than being silently coerced.

### Registration and wiring

`AddKunEntityFrameworkCore` registers the interceptor as Scoped. It does not
activate until the caller wires it into a specific `DbContextOptionsBuilder`:

```csharp
services.AddKunEntityFrameworkCore();
services.AddDbContext<CatalogDbContext>((sp, opts) =>
{
    opts.UseNpgsql(connectionString);
    opts.AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
});
```

Per-scope semantics: each resolved `CatalogDbContext` picks up the scope's
`ITenantContext`, so each request gets the tenant id scoped to itself.

---

## Database-side policy template

The interceptor only sets the session variable — it does not create RLS
policies. Those live with the service's schema migrations. Template for a
tenant-scoped table `orders`:

```sql
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
-- FORCE is critical: without it the table owner (the account running
-- migrations) bypasses RLS and all queries from the service would ignore
-- the policy. FORCE makes the policy apply even to the owner.
ALTER TABLE orders FORCE ROW LEVEL SECURITY;

CREATE POLICY orders_tenant_scope ON orders
    USING      (tenant_id = current_setting('app.current_tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', true));
```

- `USING` — rows visible to `SELECT`/`UPDATE`/`DELETE` operations.
- `WITH CHECK` — rows permitted by `INSERT`/`UPDATE` operations.

Without `WITH CHECK`, a user could insert a row belonging to another tenant,
then lose sight of it via `USING`.

The `true` second argument to `current_setting` is *missing_ok* — PostgreSQL
returns an empty string instead of raising if the variable is unset. Pair with
a `NOT NULL` constraint on `tenant_id` so the empty-string case does not match
real rows.

---

## Tests

`tests/Kun.IntegrationTests/MultiTenancy/TenantRlsTests.cs` spins a Postgres 16
container via Testcontainers and verifies:

- **RLS isolates rows without the EF filter**: the test entity deliberately
  does *not* implement `IMultiTenant`, so EF's global query filter cannot
  account for the observed tenant isolation — any separation comes entirely
  from the RLS policy driven by the interceptor.
- **Cross-tenant writes are rejected**: inserting a row with another tenant's
  id surfaces a `DbUpdateException` (Postgres code `42501 / 23514`).
- **Unsafe tenant ids are blocked before hitting the server**: a tenant id
  containing `'; DROP TABLE ...; --` trips the whitelist and throws
  `InvalidOperationException`.

---

## Operational notes

- **Connection pooling**: the `ConnectionClosingAsync` RESET ensures a pooled
  connection returned to Npgsql does not carry the previous request's tenant
  scope into the next borrower. A failure to RESET is logged (warning) rather
  than thrown, because the connection is about to close anyway and we do not
  want to convert a close-path hiccup into a user-visible error.
- **Providers other than PostgreSQL**: the interceptor is a no-op. MySQL's
  user-defined variables use different syntax (`SET @...`) and MySQL does not
  have first-class RLS; if you need equivalent enforcement there, layer a
  session-level procedure or a column-level check constraint.
- **Non-tenant-scoped connections**: when a request has no tenant context
  (e.g. an admin console running under `NullTenantContext`), the interceptor
  emits `RESET` so the session variable is cleared. Any RLS policy evaluating
  against `current_setting('app.current_tenant_id', true)` will then match
  only rows whose `tenant_id` equals the empty string — typically nothing.


## 后台链路与生产装配（S1-KUN-001）

```csharp
services.AddKunSecurity();
services.AddKunMultiTenancy(options => options.Production = true);
services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
services.AddKunTenantRls<AppDbContext>();

app.UseAuthentication();
app.UseKunMultiTenancy();
```

`AppDbContext` 继承 `KunDbContext`，或实现 `IKunMultiTenantDbContext`。泛型 RLS 入口自动附加两种拦截器与缓存键；无泛型旧重载只注册拦截器，保留既有手动装配的兼容性。生产 HTTP 链路只读取已认证身份的唯一 `tenant_id`，值需满足 1–128 个 ASCII 字母、数字、点、下划线或连字符；无令牌、非法令牌、缺失或非法租户均返回 401，不回退请求头或 `default`。匿名端点应显式放在租户中间件之外。

`AddKunSecurity` 和 `AddKunMultiTenancy` 无论先后注册，`ITenantContext` 都解析为同一 `AsyncLocalTenantContext` 单例。原 `JwtTenantContext` 仍保留公共类型以兼容既有代码，但 DI 只把它作为 `ITenantIdSource` 注册；显式作用域覆盖 HTTP 来源，显式空作用域会屏蔽 HTTP 来源。第三方旧 `ITenantContext` 实现仍可编译；需要后台回灌时须实现 `BeginScope`，默认接口方法会明确拒绝不支持的操作。

消费端在解析 handler 及其 scoped 依赖前，从信封恢复 `MessageContext.TenantId` 并打开作用域；多租户宿主的缺失或非法租户消息在幂等标记前进入死信队列。租户标识是内部可信发布者的信封元数据，不应直接接收外部请求字段；消息代理权限仍需由宿主限制。Outbox 发布器逐条恢复租户，Saga 执行器对每条队列记录创建独立 DI scope，执行与补偿均以 `SagaInstance.TenantId` 打开租户作用域，异常后也会释放。

在自定义后台任务中用 `using (tenantContext.BeginScope(tenantId))` 包围处理逻辑；不要把某个租户的 DbContext 保存为单例。PgBouncer 事务池下仍需显式事务，自动装配不改变其事务边界要求。

验收证据位于 `BackgroundTenantFlowTests`（真实 PG + RabbitMQ，A/B 交替、EF 与 RLS 独立断言、无租户／非法租户死信）、`ProductionTenantTests`（真实 RSA JWT 验证）、`TenantContextRegistrationTests`、`SagaTenantScopeTests` 与 `OutboxPublisherBackgroundServiceTests`。
