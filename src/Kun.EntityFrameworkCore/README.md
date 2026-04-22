# Kun.EntityFrameworkCore

EF Core 10 write-side persistence for the Kun Framework.

## What's in the package

- `KunDbContext` — abstract base with a tenant global filter, a soft-delete
  filter, the Outbox `DbSet<OutboxMessage>`, and the
  `SchemaConsistencyInterceptor` safety net (auto-registered).
- `EfUnitOfWork` — commits aggregate changes, dispatches domain events, and
  flushes the Outbox accumulator in a single transaction.
- `EfRepository<T>` + `SpecificationEvaluator` — write-side repository and
  specification-pattern query evaluator.
- `IntegrationEventOutboxAccumulator` — scoped bridge that implements both
  `IIntegrationEventBus` and `IOutboxWriter`, so aggregates and non-aggregate
  enqueuers (e.g. audit interceptors) share a single in-request buffer.
- `TenantRlsTransactionInterceptor` — opt-in (`AddKunTenantRls()`) defence-in-depth
  for Postgres Row-Level Security (P1-4). Emits
  `SELECT set_config('app.current_tenant_id', @tenant, true)` at each
  `TransactionStartedAsync` so RLS policies can enforce isolation even when a
  caller bypasses the EF global query filter.
- `UseKunSchema` extension — wires `MigrationsHistoryTableSchema` through the
  relational provider, for schema-per-service deployments. See
  [docs/schema-per-service.md](../../docs/schema-per-service.md) in the repo.

## Requirements

- .NET 10 SDK `10.0.100` or newer.
- `Npgsql.EntityFrameworkCore.PostgreSQL` as the provider (the framework is
  relational-provider-agnostic, but `UseKunSchema` and the Outbox poller rely
  on Postgres-specific features in practice).
- Central Package Management is on at the solution level; do NOT add a
  `Version="..."` attribute to your `<PackageReference>` — version pins live
  in `Directory.Packages.props`.

## PgBouncer transaction-pooling compatibility

When you front Postgres with PgBouncer in `POOL_MODE=transaction` (the
production default for most Kun deployments), the backend connection is
recycled between transactions. Two Npgsql defaults break under this regime
and MUST be turned off in the connection string:

| Parameter | Required value | Why |
|---|---|---|
| `No Reset On Close` | `true` | PgBouncer itself resets session state; letting Npgsql send `DISCARD ALL` on close wastes a round-trip and can conflict with the pooler. |
| `Max Auto Prepare` | `0` | Auto-prepared statements reference backend-side names that do not survive the connection swap — you'll see `prepared statement does not exist` under load. |

Minimum working PgBouncer connection string:

```
Host=pgbouncer;Port=6432;Database=kun;Username=kun;Password=***;
No Reset On Close=true;Max Auto Prepare=0
```

`tests/Kun.IntegrationTests/Fixtures/PgBouncerPostgresFixture.cs` applies
these automatically; consult it if you stand up your own local PgBouncer
container.

## Row-Level Security + explicit transactions

`TenantRlsTransactionInterceptor` sets the tenant GUC with
`set_config(..., is_local=true)` — the function-form equivalent of
`SET LOCAL`, so the value is scoped to the current transaction. This works
correctly **only** inside an explicit transaction. Under PgBouncer
transaction-pool, the interceptor guarantees each transaction starts fresh
and emits the GUC before business statements run.

Practical consequences:

1. **Always run RLS-protected writes inside a transaction.** The Mediator
   `UnitOfWorkPipelineBehavior` wraps every Command handler in an EF
   `SaveChangesAsync` call, which opens its own transaction, so this is the
   default for Command flows.
2. **Auto-commit SELECTs under PgBouncer do NOT get the GUC.** If you query
   via Dapper against the same Postgres instance without an explicit
   transaction, the RLS policy sees no `app.current_tenant_id` setting. EF
   policies in this repo are written to evaluate
   `current_setting('app.current_tenant_id', true)` with `missing_ok=true`,
   which returns NULL in that case and filters out every row — fail-closed.
3. **Admin paths that genuinely need cross-tenant access** must either not
   opt into `AddKunTenantRls()` or run against a connection that bypasses
   the policy (`ALTER TABLE ... NO FORCE ROW LEVEL SECURITY` on an admin
   role, for example).

The fixture and the acceptance test are in
`tests/Kun.IntegrationTests/Persistence/`:

- `RlsEnforcementTests` — direct-to-Postgres proof that policies block
  cross-tenant reads even with `IgnoreQueryFilters()`.
- `TenantRlsInterceptorPgBouncerTests` (M2) — same proof through PgBouncer
  transaction-pool.
- `PgBouncerCompatTests` — 100-parallel-query stress loop that would fail
  with `prepared statement does not exist` if the compatibility flags were
  ever dropped.

## Dev checklist

- Override `KunDbContext.DefaultSchema` in your service's DbContext AND call
  `.UseKunSchema("your-schema")` on the options builder. Forgetting either
  side trips `SchemaConsistencyInterceptor` at the first save.
- Register services via `AddKunEntityFrameworkCore()` — wires the UoW,
  Outbox accumulator, and the default `InProcessDomainEventDispatcher`.
- Opt into RLS with `AddKunTenantRls()` when the database side has policies
  defined. Not a default because it adds one `set_config` round-trip per
  transaction.
- Never introduce new `PackageReference` to `Kun.Core` — the framework
  enforces this via `scripts/check-layering.sh` in CI.

## See also

- [docs/schema-per-service.md](../../docs/schema-per-service.md) — schema
  override + migrations regeneration walkthrough.
- [docs/guides/read-side-patterns.md](../../docs/guides/read-side-patterns.md) —
  the Dapper read-side pairs with this package via CQRS.
- [CLAUDE.md](../../CLAUDE.md) §"Outbox (atomicity is the whole point)" — the
  architectural invariants this package enforces.
