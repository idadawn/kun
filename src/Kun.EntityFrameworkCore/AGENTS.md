<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.EntityFrameworkCore

## Purpose

Write-side persistence: EF Core 10 implementation of `IRepository<T>` and `IUnitOfWork`, `KunDbContext` with automatic multi-tenant query filters and soft-delete filters, Outbox message table + atomic `SaveChanges`-time enqueuing of domain events as Outbox rows, `SpecificationEvaluator` that translates `ISpecification<T>` into `IQueryable<T>` predicates.

## Key Files

| File | Description |
|------|-------------|
| `Kun.EntityFrameworkCore.csproj` | `IsAotCandidate=false` (trim-safe only until EF Core AOT matures) |

## For AI Agents

### Working In This Directory

- **This module is not AOT-strict** — it's the one exception in the framework. Treat it as trim-safe: avoid obvious reflection hotspots, but EF Core's internal reflection usage is accepted until .NET ships full AOT providers.
- **All persistence mapping lives here**, via `IEntityTypeConfiguration<T>`. Do not add `[Table]`/`[Column]`/`[Key]` attributes to domain types in `Kun.Core`.
- **Outbox atomicity is the core invariant**: `UnitOfWork.SaveChangesAsync` must collect domain events from `ChangeTracker.Entries<IAggregateRoot>()`, convert to `OutboxMessage` with stable event IDs + trace context, and let `DbContext.SaveChangesAsync` commit them in the same transaction as aggregate changes. Never split these into separate transactions.
- **Tenant filters** are attached in `KunDbContext.OnModelCreating` for any entity implementing `IMultiTenant`. Avoid `IgnoreQueryFilters()` except in clearly marked admin-only code paths.
- Event payloads serialize via `KunJsonContext.Default.GetTypeInfo(type)!` — never `JsonSerializer.Serialize(obj)` without an explicit `JsonTypeInfo`.

### Testing Requirements

Cross-layer behavior (Outbox atomicity, tenant isolation, concurrency) is exercised in `tests/Kun.IntegrationTests` via Testcontainers PostgreSQL. Pure mapping bugs are not worth integration tests — catch them in reviewed EF migrations.

### Common Patterns

- `EfRepository<T>` delegates listing to `SpecificationEvaluator.Default.GetQuery(set, spec)`.
- `KunDbContext` is abstract; business `DbContext`s derive from it and add `DbSet<T>` plus their own `IEntityTypeConfiguration<T>` registrations.

## Dependencies

### Internal

- `Kun.Core` — aggregates, tenant/audit markers, events.
- `Kun.Infrastructure` — `IRepository<T>`, `IUnitOfWork`, `ISpecification<T>`.
- `Kun.EventBus` — `OutboxMessage`, `IEventTypeRegistry`.

### External

- `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`
- `Npgsql.EntityFrameworkCore.PostgreSQL` (default), `Pomelo.EntityFrameworkCore.MySql` (alt)

<!-- MANUAL: -->
