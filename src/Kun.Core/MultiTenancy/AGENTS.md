<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# MultiTenancy

## Purpose

`ITenantContext` — the abstraction every tenant-scoped component depends on. Concrete implementations live at higher layers: `JwtTenantContext` (in `Kun.Security`, reads `tenant_id` claim from the validated S2S JWT), background-task implementations that read from configuration or message headers.

## Key Files

| File | Description |
|------|-------------|
| `ITenantContext.cs` | `TenantId` (nullable string) and `IsMultiTenant` (whether the host app turned on multi-tenancy at all) |

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
