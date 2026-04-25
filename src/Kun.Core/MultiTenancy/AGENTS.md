<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-23 -->

# MultiTenancy

## Purpose

`ITenantContext` — the abstraction every tenant-scoped component depends on. Concrete implementations live at higher layers: `JwtTenantContext` (in `Kun.Security`, reads `tenant_id` claim from the validated S2S JWT), background-task implementations that read from configuration or message headers.

## Key Files

| File | Description |
|------|-------------|
| `ITenantContext.cs` | `TenantId` (nullable string) and `IsMultiTenant` (whether the host app turned on multi-tenancy at all) |
| `NullTenantContext.cs` | Singleton no-op for non-multi-tenant hosts / background jobs |
| `TenantIsolationStrategy.cs` | Enum — `Shared` / `SchemaPerTenant` / `DatabasePerTenant` (P2-D forward-looking contract) |
| `TenantInfo.cs` | Immutable tenant DTO used by `ITenantProvisioner` |
| `ITenantProvisioner.cs` | Idempotent lifecycle hook — open / offboard a tenant |
| `NullTenantProvisioner.cs` | Default no-op provisioner for Shared strategy |

## P2-D Multi-Tenancy Strategy Architecture (Part 1 landed 2026-04-23)

`TenantIsolationStrategy` + `ITenantProvisioner` are forward-looking contracts from P2-D Part 1. They plug into the existing `ITenantContext` + `IMultiTenant` stack:

- **Shared** (already shipped via `AddKunTenantRls()`): `TenantContext.TenantId` fills the global query filter on `IMultiTenant` entities and, when `AddKunTenantRls()` is registered, is pushed to PostgreSQL RLS via `set_config('app.current_tenant_id', ...)` on each transaction. `NullTenantProvisioner` is the appropriate default — Shared doesn't need per-tenant setup.
- **SchemaPerTenant** (Part 2, pending): a future `ITenantDbContextFactory<TContext>` will consume `ITenantContext.TenantId` and build a `DbContext` with its `DefaultSchema` / `MigrationsHistoryTable.Schema` scoped to the tenant. A non-null `ITenantProvisioner` implementation creates the schema at tenant signup.
- **DatabasePerTenant** (Part 3, pending): a future `IDbConnectionResolver` resolves the tenant's connection string from `ITenantContext.TenantId`. A non-null `ITenantProvisioner` creates the database at tenant signup.

See `docs/architecture/adr-p2d-multitenancy.md` for deferred decisions and tradeoff discussion.

## For AI Agents

### Working In This Directory

- **Interface only, zero implementation.** Concrete contexts live in `Kun.Security` (HTTP/gRPC requests), `Kun.EventBus.RabbitMQ` (message-consumer context), and downstream application hosts (background jobs).
- **`TenantId` is nullable** — legitimate scenarios (system maintenance jobs, cross-tenant admin) have no tenant. Consumers must decide their own policy when encountering `null`.
- **Do not extend `ITenantContext` with arbitrary claims.** User-identity claims belong on `ICurrentUser` (in `../Security/`). Adding `UserId` / `Roles` here blurs the split.

### Common Patterns

- `KunDbContext.OnModelCreating` auto-attaches a query filter `e => !e.IsDeleted && e.TenantId == _tenantContext.TenantId` to every `IMultiTenant` entity.
- Redis cache keys include `TenantId` to avoid cross-tenant poisoning.

## Dependencies

### Internal

None.

<!-- MANUAL: -->
