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
