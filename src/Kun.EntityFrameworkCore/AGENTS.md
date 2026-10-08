<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.EntityFrameworkCore

## Purpose

Write-side persistence for Kun Framework: `KunDbContext` base with automatic `IMultiTenant` tenant filters and `ISoftDelete` soft-delete filters, atomic Outbox integration, and a unit-of-work pipeline behavior that commits aggregate changes + integration events + domain events in a single transaction.

## Key Files

| File | Description |
|------|-------------|
| `KunDbContext.cs` | Base DbContext with `IMultiTenant` + `ISoftDelete` global query filters and Outbox DbSet |
| `OutboxMessageConfiguration.cs` | EF mapping for `OutboxMessage` (no ORM attrs on the entity) |
| `IUnitOfWork.cs` / `EfUnitOfWork.cs` | Atomic SaveChanges: aggregates + outbox + domain-event dispatch |
| `IntegrationEventOutboxAccumulator.cs` | `IIntegrationEventBus` impl — serializes events, captures TraceParent, appends to buffer |
| `Behaviors/UnitOfWorkPipelineBehavior.cs` | Mediator behavior: calls `SaveChangesAsync` after handler returns |
| `NoOpDomainEventDispatcher.cs` | Default no-op dispatcher (users replace with their own implementation) |
| `Interceptors/TenantRlsConnectionInterceptor.cs` | Defence-in-depth: pushes `ITenantContext.TenantId` into `SET app.current_tenant_id` on PG connection open; RESET on close. Opt-in via `opts.AddInterceptors(...)` on the caller's `DbContextOptionsBuilder`. |
| `DependencyInjection/KunEntityFrameworkCoreServiceCollectionExtensions.cs` | `AddKunEntityFrameworkCore` DI entry point |

## For AI Agents

### Working In This Directory
- **NOT AOT-candidate** (`<IsAotCandidate>false</IsAotCandidate>`) — EF Core uses runtime expression trees. Do not add AOT annotations.
- All persistence mapping lives in `IEntityTypeConfiguration<T>` — never add `[Table]`/`[Column]`/`[Key]` attributes to domain types in `Kun.Core`.
- `EfUnitOfWork.SaveChangesAsync` is the single transaction boundary. Domain event collection, outbox flush, and `DbContext.SaveChangesAsync` must stay in one call — do not split.
- `UnitOfWorkPipelineBehavior` should be registered after validation behaviors, before logging behaviors.
- `IMultiTenant` and `ISoftDelete` are both in the `Kun.Core.Domain` namespace (not `Kun.Core.MultiTenancy`).
- Domain event dispatch (via `IDomainEventDispatcher`) happens AFTER `DbContext.SaveChangesAsync` — events are in-process and non-transactional.
- `IntegrationEventOutboxAccumulator` uses `JsonSerializer.Serialize<TEvent>` (generic overload) — NOT the non-generic overload, to satisfy CA2263.
- `TenantRlsConnectionInterceptor` is **opt-in**. The EF global query filter in `KunDbContext` is the first isolation layer; the interceptor is a *second* layer that pairs with RLS policies defined on the database schema. Wiring is explicit — `AddKunEntityFrameworkCore` registers the interceptor as Scoped but does not attach it to any `DbContextOptionsBuilder`. Callers do that per context: `opts.AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>())`. See `docs/security/multi-tenancy-rls.md` for the policy template.

### Testing Requirements
Integration tests require Docker + Testcontainers and are tracked separately (M2 2.4).

## Dependencies

### Internal
- `Kun.Core` — `IAggregateRoot`, `IDomainEventDispatcher`, `ITenantContext`, `IMultiTenant`, `ISoftDelete`
- `Kun.EventBus` — `OutboxMessage`, `IntegrationEventBuffer`, `IEventTypeRegistry`, `IIntegrationEventBus`
- `Kun.Mediator` — `IPipelineBehavior`

### External
- `Microsoft.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

<!-- MANUAL: -->

`AddKunTenantRls<TContext>()` 自动装配 RLS 双拦截器与 `KunMultiTenantModelCacheKeyFactory`；上文手动接线说明仅适用于无泛型旧入口。`KunDbContext` 已实现 `IKunMultiTenantDbContext`。
