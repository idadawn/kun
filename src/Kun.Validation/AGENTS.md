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
- **Validation failures return `Result.Fail(Error.Validation(...))`, not exceptions.** The pipeline behavior converts validator results into the Mediator response before the handler runs.
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
