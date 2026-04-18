<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Cqrs

## Purpose

CQRS request and handler contracts. These are plain interfaces — the actual dispatcher lives in `Kun.Mediator`. Defining them in `Kun.Core` lets every layer (including `Kun.Core`'s downstream modules) reference CQRS types without any dispatcher dependency.

## Key Files

| File | Description |
|------|-------------|
| `IRequest.cs` | `IRequest<TResponse>`, `ICommand<T>`, `ICommand` (returns `Result`), `IQuery<T>`, `IRequestHandler<TRequest, TResponse>` |

## For AI Agents

### Working In This Directory

- **Contracts only, zero implementation.** No base classes that capture behavior belong here — put those in `Kun.Mediator`.
- **Separation of `ICommand<T>` vs `IQuery<T>` is deliberate.** It lets pipeline behaviors (transactions, write-side UoW commit) match on the command interface without accidentally wrapping queries.
- `ICommand` (no generic) returns `Result` as a convention — commands that don't produce data still report success/failure through `Result`, never void + exceptions.
- Handler signature uses `ValueTask<TResponse>` to stay allocation-light.

### Common Patterns

- One command class, one handler class, colocated in feature folders in application code (not in `Kun.Core`).
- Query handlers go through `Kun.Dapper` for read-side, command handlers through `Kun.EntityFrameworkCore` repositories for write-side.

## Dependencies

### Internal

- `Kun.Core.Results` — `Result` / `Result<T>` for the `ICommand` non-generic convention.

<!-- MANUAL: -->
