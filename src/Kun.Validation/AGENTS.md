<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Validation

## Purpose

Source-generated request validation. Validators emit validation logic at compile time so there is no runtime reflection (FluentValidation's default mode is not AOT-friendly). Validators plug into the Mediator pipeline as a `ValidationBehavior` that short-circuits with `Result.Fail(Error.Validation(...))` when a command fails validation.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Validation.csproj` | `IsAotCandidate=true` |

## For AI Agents

### Working In This Directory

- **Validators are written per-command type** and annotated/discovered by the source generator. Runtime reflection-based validator discovery (e.g. `ValidatorRegistry.Scan()`) is not allowed — the generator builds the registry at compile time.
- **Two pipeline behaviors ship** — pick one per request type:
  - `ValidationPipelineBehavior<TRequest, TResponse>` throws
    `ValidationException` on validator failure. Handlers either
    return their plain response or let the exception escape to
    ASP.NET / gRPC translation middleware.
  - `ValidationResultPipelineBehavior<TRequest, TValue>` returns
    `Result<TValue>.Fail(Error.Validation("request.invalid", ...))`
    instead of throwing. Preferred for handlers that compose via
    `Result<T>` — matches the framework-wide "business failures use
    `Result`, not exceptions" rule in `kun/CLAUDE.md`
    §Architectural constraints. See
    [ADR-014](../../docs/architecture/ADR-014-Validation-Result-behavior.md).
  - **Do not register both for the same request type** — picking
    both produces double-wrapping of validation messages. Runtime
    diagnostic was deemed too expensive for a rare misconfiguration;
    doc guardrail only.
- Keep validator logic pure (no DB calls). Semantic validation that needs data belongs in the handler, not here.

### Testing Requirements

Validator unit tests are straightforward: construct request, invoke validator, assert `Result` / collected errors. Belongs near this module or in the consuming command's test project.

### Common Patterns

- One validator class per command: `CreateOrderCommandValidator : IValidator<CreateOrderCommand>`.
- Error codes follow the `{aggregate}.{field}.{constraint}` pattern (e.g. `order.quantity.greaterThanZero`).

## Dependencies

### Internal

- `Kun.Core` — `Result`, `Error`.

### External

- Source-generator tooling (Roslyn analyzers / incremental source generators are shipped with the module).

<!-- MANUAL: -->
