---
id: KN-SELF-007
title: Kun.Validation Result-returning pipeline behavior (ADR-011 consumer)
status: done
planner_commit: b388bb1
executor_head: 2d7cc50
estimated_commits: 5
actual_commits: 5
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-007 — `Kun.Validation` Result-returning pipeline behavior

## Why

`src/Kun.Validation/Behaviors/ValidationPipelineBehavior.cs` ships
today as an **exception-throwing** variant: if any validator reports
errors, the pipeline throws `ValidationException`. Its own XML doc
carries a TODO:

> For Result-based semantics (returning `Result.Fail(Error.Validation(...))`
> instead of throwing), compose this behavior inside a user-written
> "ValidationResultAdapter" behavior — **shipped as a follow-up**.

KN-SELF-001 landed the `Result<T>` composition extensions that make
a Result-returning handler ergonomic. KN-SELF-007 ships the missing
half: a `ValidationResultPipelineBehavior<TRequest, TValue>` that
returns `Result<TValue>.Fail(Error.Validation(...))` instead of
throwing, so handlers can stay exception-free.

## Specs to read first

1. `kun/CLAUDE.md` — §Architectural constraints; especially the
   "business failures use `Result` / `Result<T>`, not exceptions"
   rule — KN-SELF-007 is exactly the enablement for that rule at
   the validation stage.
2. `kun/docs/architecture/ADR-011-Result-async-extensions.md` —
   Result<T> composition; this behavior's return value is the
   input consumers chain against.
3. `kun/src/Kun.Validation/Behaviors/ValidationPipelineBehavior.cs`
   — the existing throwing variant; mirror its loop shape.
4. `kun/src/Kun.Validation/IValidator.cs`, `ValidationResult.cs`,
   `ValidationError.cs` — validator contract + error DTO.
5. `kun/src/Kun.Validation/DependencyInjection/` — the registration
   pattern (`AddValidator<TRequest, TValidator>` /
   `AddValidationBehavior<TRequest, TResponse>`).
6. `kun/src/Kun.Mediator/IPipelineBehavior.cs` — behavior contract;
   `ValueTask<TResponse> Handle(TRequest, RequestHandlerDelegate<TResponse>, CancellationToken)`.
7. `kun/src/Kun.Core/Results/Error.cs` — `Error.Validation(code,
   message)` factory — where the failure Error comes from.

## Acceptance criteria

- **AC-1**: `docs/architecture/ADR-014-Validation-Result-behavior.md`
  accepted. Short ADR (~60 lines). Records:
  - Design choice to ship **only the `Result<TValue>` variant** in
    this task, not the non-generic `Result` variant. Rationale: most
    commands return a value; the void case (a pure command with no
    data to return) can stay on the throwing variant until demand
    for a `Result`-only variant surfaces. Tracked as F1.
  - Why the behavior requires explicit per-request-type DI
    registration via `AddValidationResultBehavior<TRequest, TValue>()`
    — generic-constraint-on-`TResponse` cannot be expressed at the
    open-generic DI registration level; matches the existing
    `AddValidator<TRequest, TValidator>` explicit-registration
    pattern.
  - Coexistence rule with the throwing `ValidationPipelineBehavior`:
    register one or the other per request type, never both. If both
    are registered, the first-seen wins and a diagnostic is
    unfortunately not cheap to add — keep the guardrail as doc until
    someone hits it in practice.
  - AOT safety: no reflection, `typeof(TResponse)` is compile-time-
    known per closed generic, `Error.Validation` is pure BCL.
- **AC-2**: `src/Kun.Validation/Behaviors/ValidationResultPipelineBehavior.cs`
  — sealed generic class `ValidationResultPipelineBehavior<TRequest, TValue>`
  implementing `IPipelineBehavior<TRequest, Result<TValue>>` with
  constraint `where TRequest : IRequest<Result<TValue>>`. Mirrors
  the existing throwing variant's validator loop but returns
  `Result<TValue>.Fail(Error.Validation("request.invalid", …))`
  when errors are present. Empty-validators fast-path.
- **AC-3**: `AddValidationResultBehavior<TRequest, TValue>()`
  extension on `KunMediatorBuilder` — registers the closed-generic
  behavior. Matches the style of `AddValidationBehavior<TRequest,
  TResponse>` already in the module's DI extensions. PublicAPI
  entry added in-commit.
- **AC-4**: `tests/Kun.Validation.Tests` (or equivalent) gains
  coverage:
  - Empty validators → pass-through, handler result returned.
  - Single validator reports 0 errors → handler result returned.
  - Single validator reports N errors → `Result<TValue>.Fail` with
    concatenated message + `request.invalid` code.
  - Multiple validators all report errors → errors aggregated.
  - Handler-level `Result<TValue>` failure (when validators all
    pass) is NOT intercepted — the behavior returns `next()`'s
    actual Result, not a derived failure.
- **AC-5**: `src/Kun.Validation/AGENTS.md` gains a "Result vs
  exception" subsection pointing at ADR-014; `CHANGELOG.md
  [Unreleased]` gets a "### Added" entry.

## Commit plan

| # | Commit subject | AC | Notes |
|---|----------------|----|-------|
| 1 | `docs(validation): ADR-014 Result-returning validation behavior` | AC-1 | ADR-first |
| 2 | `feat(validation): ValidationResultPipelineBehavior<TRequest, TValue>` | AC-2 | behavior class + PublicAPI.Unshipped entry |
| 3 | `feat(validation): AddValidationResultBehavior DI extension` | AC-3 | KunMediatorBuilder extension + PublicAPI entry |
| 4 | `test(validation): Result-returning behavior covers 5 scenarios` | AC-4 | xUnit v3, hand-rolled validator fakes, no Testcontainers |
| 5 | `docs(validation): AGENTS + CHANGELOG for Result variant` | AC-5 | wrap-up |

## AOT / build gates

- [ ] `dotnet format` on touched projects
- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0/0
- [ ] `dotnet test tests/Kun.Validation.Tests -c Release --no-build` green
- [ ] `dotnet publish src/Kun.Validation -c Release -r linux-x64
      -p:PublishAot=true /warnaserror` ILC 0 warning
- [ ] No new `<PackageReference>` on `Kun.Validation.csproj`
- [ ] Existing `ValidationPipelineBehavior` tests still green
      (coexistence check)

## Out of scope

- `ValidationResultPipelineBehavior<TRequest>` for non-generic
  `Result` response — tracked as ADR-014 F1; ships when demand
  appears.
- Automatic selection between throwing and Result-based variants —
  consumers pick per request type.
- Validation error **localization** — errors carry the message
  strings the validators produce.

## Progress log

- 2026-04-24 4f6cd22 KN-SELF-006 closed (prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-007 into BACKLOG `## Done`.
3. `echo "KN-SELF-007 done (<SHA>) — Result-returning validation behavior landed." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-007_done.txt`
4. Planner next pick: per-module `HasPublicApi=true` flip sweep
   (large, L-size, may need split), or look for a fresh M-sized
   follow-up surfaced during this task.
