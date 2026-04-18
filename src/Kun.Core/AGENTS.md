<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Core

## Purpose

Framework core layer. Contains domain primitives (aggregate roots, entities, value objects, domain events), CQRS request contracts, the `Result` pattern, multi-tenancy abstraction, and the current-user abstraction. **Zero third-party dependencies** — only BCL. Every other Kun module builds on this one.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Core.csproj` | `IsAotCandidate=true`, `IsPackable=true`, no `PackageReference`s |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `Cqrs/` | `IRequest<T>`, `ICommand`, `IQuery`, `IRequestHandler` (see `Cqrs/AGENTS.md`) |
| `Domain/` | `Entity<TKey>`, `AggregateRoot<TKey,TUserKey>`, `ValueObject`, `IDomainEvent`, `IIntegrationEvent`, audit/soft-delete/multi-tenant markers (see `Domain/AGENTS.md`) |
| `MultiTenancy/` | `ITenantContext` (see `MultiTenancy/AGENTS.md`) |
| `Results/` | `Result`, `Result<T>`, `Error`, `ErrorType`, `ResultFailureException` (see `Results/AGENTS.md`) |
| `Security/` | `ICurrentUser` (see `Security/AGENTS.md`) |

## For AI Agents

### Working In This Directory

- **The zero-dependency rule is absolute**: do not add `<PackageReference>` to this csproj, not even for `Microsoft.Extensions.*`. Anything that needs DI abstractions, options, or logging must live in `Kun.Mediator`, `Kun.Infrastructure`, or a higher layer.
- **No ORM, JSON, or validation attributes** on domain types. `[Table]`, `[Column]`, `[JsonPropertyName]`, `[Required]`, etc. all belong in the infrastructure or validation layer. `AggregateRoot` + `IEntityTypeConfiguration<T>` (in `Kun.EntityFrameworkCore`) is the split.
- **AOT strict**: no reflection, no `MakeGenericType`, no parameterless `JsonSerializer.Serialize`. Since there are no packages here and no serialization is performed, this should naturally hold — be alert if you ever add infrastructure code here (you probably shouldn't).
- Use `ArgumentNullException.ThrowIfNull(arg)` for null guards on public APIs.

### Testing Requirements

Tests for types defined here live in `tests/Kun.Core.Tests`. They are pure in-memory — no containers, no I/O. Assert aggregate invariants, value-object equality, and `Result` success/failure semantics.

### Common Patterns

- Aggregates raise domain events via `AddDomainEvent(...)`; `UnitOfWork.SaveChangesAsync` (in `Kun.EntityFrameworkCore`) collects and converts them to Outbox messages.
- Values objects override `GetEqualityComponents()` and are treated as immutable — mutations return new instances.
- `Result<T>` has implicit conversions from `T` and `Error` so handler code stays terse.

## Dependencies

### Internal

None — `Kun.Core` is the root.

### External

None — BCL only.

<!-- MANUAL: -->
