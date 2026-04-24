---
id: KN-SELF-012
title: Non-generic Result variant of ValidationResultPipelineBehavior (ADR-014 F1)
status: in-flight
planner_commit: <fill after commit>
executor_head: <fill after first executor commit>
estimated_commits: 3
started: 2026-04-24
---

# KN-SELF-012 — non-generic `Result` variant of `ValidationResultPipelineBehavior`

## Why

KN-SELF-007 shipped `ValidationResultPipelineBehavior<TRequest, TValue>`
for handlers that return `Result<TValue>`. ADR-014 §F1 deferred the
non-generic `Result` variant (for commands with no return value) on
the grounds that most handlers return a value. In practice, a
meaningful minority of commands in `neuroagent-csharp` and the
samples DO return plain `Result` (e.g. "delete user" / "mark
read") — and today those have to either throw
`ValidationException` (the existing throwing variant) or hand-roll
a try/catch adapter.

Close F1 with a mirror implementation. The code is a near-copy of
the generic variant; the DI extension overload is a thin
companion.

## Specs to read first

1. `kun/docs/architecture/ADR-014-Validation-Result-behavior.md`
   §F1 — the follow-up this task closes.
2. `src/Kun.Validation/Behaviors/ValidationResultPipelineBehavior.cs`
   — shape to mirror.
3. `src/Kun.Validation/DependencyInjection/KunValidationServiceCollectionExtensions.cs`
   — DI extension pattern.
4. `tests/Kun.Validation.Tests/ValidationResultPipelineBehaviorTests.cs`
   — test pattern to mirror.

## Acceptance criteria

- **AC-1**: `src/Kun.Validation/Behaviors/ValidationResultPipelineBehavior.cs`
  gains a sibling non-generic class
  `ValidationResultPipelineBehavior<TRequest>` where
  `TRequest : IRequest<Result>`. Same validator loop, same
  `Error.Validation("request.invalid", joined-message)` output;
  returns `Result.Fail(...)` instead of `Result<TValue>.Fail(...)`.
  Lives in the same file to keep the Result-returning pair
  co-located; PublicAPI entries added in-commit.
- **AC-2**: DI extension
  `AddValidationResultBehavior<TRequest>()` (single-type-param
  overload) in the existing
  `KunValidationServiceCollectionExtensions`. Closes for
  `IRequest<Result>` specifically.
- **AC-3**: `tests/Kun.Validation.Tests/ValidationResultPipelineBehaviorTests.cs`
  gains 3 tests mirroring the key generic-variant scenarios for
  the non-generic path: validator-fail → `Result.Fail` with
  `request.invalid`, handler-level `Result.Fail` preserved,
  multi-validator aggregation.
- **AC-4**: `CHANGELOG.md [Unreleased]` references ADR-014 §F1
  resolved; ADR-014 §F1 bullet annotated "Resolved 2026-04-24
  (KN-SELF-012)".

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `feat(validation): non-generic Result variant of ValidationResultPipelineBehavior + DI extension` | AC-1 + AC-2 combined (sibling class + extension overload in existing files) |
| 2 | `test(validation): cover non-generic Result variant of ValidationResultPipelineBehavior` | AC-3 |
| 3 | `docs(validation): ADR-014 §F1 resolved + CHANGELOG` | AC-4 |

## AOT / build gates

- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0/0
- [ ] `dotnet test tests/Kun.Validation.Tests -c Release --no-build`
      green (17 existing + 3 new = 20)
- [ ] `dotnet publish src/Kun.Validation -c Release -r linux-x64
      -p:PublishAot=true /warnaserror` ILC 0 warning

## Out of scope

- Structured per-field error payload (still ADR-014 §F2).
- Source-generator-based bulk registration (still ADR-014 §F3).

## Progress log

- 2026-04-24 4f06307 KN-SELF-011 closed (prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-012 to BACKLOG `## Done`.
3. `echo "KN-SELF-012 done (<SHA>) — ADR-014 §F1 resolved (non-generic Result validation behavior)." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-012_done.txt`
4. Planner next pick: ADR-015 F1 dead-letter handling, or
   ADR-012/015 F3 Kun.Testing.EntityFrameworkCore Outbox helpers,
   or fresh pain point.
