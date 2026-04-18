<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Infrastructure

## Purpose

Pure infrastructure *abstractions* — interfaces only. Repository, Unit of Work, Specification pattern, and `IProcessedMessageStore` (for consumer idempotency) are defined here so domain and application code depends on contracts, not on EF Core / Dapper / Redis directly.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Infrastructure.csproj` | `IsAotCandidate=true`, references `Kun.Core` |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `Abstractions/` | `IRepository<T>`, `IUnitOfWork`, `ISpecification<T>`, `Specification<T>` base, `IProcessedMessageStore` (see `Abstractions/AGENTS.md`) |

## For AI Agents

### Working In This Directory

- **Interfaces only.** No EF Core, Dapper, Redis, or RabbitMQ code lives here — those belong in the concrete infrastructure modules (`Kun.EntityFrameworkCore`, `Kun.Dapper`, `Kun.EventBus.RabbitMQ`).
- The `Specification<T>` base class does contain logic, but only `System.Linq.Expressions` — still BCL-only.
- Repository interface accepts `object id` to avoid constraining aggregate key types; concrete implementations can cast to `TKey`.
- `IUnitOfWork.SaveChangesAsync` is expected to commit aggregate changes **and** Outbox messages in a single transaction — see `Kun.EntityFrameworkCore` for the concrete contract fulfilment.

### Testing Requirements

There isn't a dedicated test project for this module; behavior is verified via `tests/Kun.IntegrationTests` (concrete implementations) and `tests/Kun.Core.Tests` (Specification composition logic).

### Common Patterns

- Specifications are composed in constructors via protected `AddCriteria`, `ApplyOrderBy`, `ApplyPaging`, `AddInclude` helpers; multiple `AddCriteria` calls are AND-composed.

## Dependencies

### Internal

- `Kun.Core` — `IAggregateRoot`, domain types.

### External

None (BCL only — `System.Linq.Expressions`).

<!-- MANUAL: -->
