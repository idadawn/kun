<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Mediator

## Purpose

Reflection-free CQRS dispatcher. `FrozenDictionary<Type, RequestHandlerBase>` gives O(1) handler lookup with zero runtime reflection, making this module the AOT-native alternative to MediatR (which moved to commercial licensing). Handlers are registered at compile time by a companion source generator; pipeline behaviors compose around handlers for cross-cutting concerns (logging, validation, auth, transactions, idempotency).

## Key Files

| File | Description |
|------|-------------|
| `Kun.Mediator.csproj` | `IsAotCandidate=true`, references `Kun.Core` + `Microsoft.Extensions.DependencyInjection.Abstractions` |

## For AI Agents

### Working In This Directory

- **Never use runtime reflection to resolve handlers.** The entire point of this module is compile-time registration. If you find yourself reaching for `Type.GetMethod`, `Activator.CreateInstance`, or `IServiceProvider.GetServices(handlerType)` with a runtime `Type`, stop and consult the `kun-cqrs-mediator` skill.
- Handler registration happens through a source generator emitting an `AddKunMediator(this IServiceCollection)` extension that populates the `FrozenDictionary`. New handler classes are discovered automatically at compile time — no manual registration.
- **Pipeline behaviors** wrap handlers in a middleware chain. Order matters: logging → validation → authorization → transaction → handler → (reverse on unwind). Be deliberate about where new behaviors go.
- Dispatch returns `ValueTask<TResponse>` to avoid allocation in synchronous-completed paths.

### Testing Requirements

Unit tests in `tests/Kun.Mediator.Tests` exercise: dispatcher lookup correctness, pipeline behavior ordering, cancellation propagation. No containers.

### Common Patterns

- `ISender` (send one request → one response) and `IPublisher` (publish notification → many handlers) are the public entry points.
- Pipeline behaviors implement `IPipelineBehavior<TRequest, TResponse>` and are resolved from DI.

## Dependencies

### Internal

- `Kun.Core` — CQRS contracts, `Result`.

### External

- `Microsoft.Extensions.DependencyInjection.Abstractions`

<!-- MANUAL: -->
