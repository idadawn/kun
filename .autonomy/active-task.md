---
id: KN-SELF-002
title: Kun.Testing fluent assertion helpers
status: done
planner_commit: 49b6918
executor_head: aa4b4d7
estimated_commits: 8
actual_commits: 7
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-002 — `Kun.Testing` fluent assertion helpers

## Why

Every `tests/Kun.*.Tests` project today open-codes the same three
`Result` / domain-event shapes:

```csharp
Assert.True(result.IsSuccess);
Assert.Equal("order.notFound", result.Error.Code);
Assert.Contains(aggregate.DomainEvents, e => e is OrderPlacedEvent);
```

FluentAssertions is present but the `Result<T>` / `IAggregateRoot`
shapes aren't first-class to FluentAssertions, so the same little
block gets retyped. `neuroagent-csharp` tests repeat the pattern
too — another consumer-drift risk like the one ADR-010 warned about
at the session layer and ADR-011 fixed at the Result composition
layer.

Ship a small `Kun.Testing` helper assembly with:

- `result.ShouldBeSuccess()` / `ShouldBeSuccess(out TValue value)`
- `result.ShouldBeFailure()` / `ShouldHaveErrorCode("order.notFound")`
  / `ShouldHaveErrorType(ErrorType.NotFound)`
- `aggregate.ShouldRaise<TEvent>()` (single expected event, returns
  the event for further inspection)
- `aggregate.ShouldRaiseExactly<TEvent>(int count)` / `ShouldNotRaise<TEvent>()`

Explicitly **out of scope** for this task: Outbox message assertions
(Outbox lives in `Kun.EntityFrameworkCore` which is non-AOT; if/when
needed, ships as a separate `Kun.Testing.EntityFrameworkCore`
companion — tracked as KN-SELF-002 follow-up in BACKLOG).

## Specs to read first (non-negotiable)

Required by `kun/CLAUDE.md` §Specs-first:

1. `kun/CLAUDE.md` — §Specs-first + §Four-layer dependency flow
   (confirm `Kun.Testing` sits at the Abstraction layer — references
   `Kun.Core` only)
2. `kun/AGENTS.md`
3. `kun/CONTRIBUTING.md`
4. `kun/src/Kun.Core/AGENTS.md` — zero-third-party-dep rule applies
   by extension (`Kun.Testing` should take no non-BCL runtime deps
   beyond `Kun.Core`)
5. `kun/src/Kun.Core/Results/AGENTS.md` — understand the `Result`
   surface we're asserting against (updated in KN-SELF-001)
6. `kun/src/Kun.Core/Domain/IAggregateRoot.cs`,
   `Domain/IDomainEvent.cs` — shape of what `ShouldRaise` asserts on
7. `kun/tests/Kun.Core.Tests/AGENTS.md` — testing conventions (xUnit
   v3, FluentAssertions, AAA)
8. `kun/Directory.Build.props` + `kun/Directory.Packages.props`
9. ADR-010, ADR-011 — for voice/structure when writing ADR-012

## Acceptance criteria

- **AC-1**: `docs/architecture/ADR-012-Kun.Testing-module.md` accepted.
  Records: module placement at the Abstraction layer, zero-runtime-
  third-party-dep constraint (depends only on `Kun.Core`), choice of
  throwing a custom `KunAssertionException` rather than binding to a
  specific test framework (xUnit vs NUnit vs MSTest) so the helpers
  are framework-agnostic, and the explicit out-of-scope list (Outbox,
  Testcontainers fixtures, HTTP test servers).
- **AC-2**: Scaffold `src/Kun.Testing` project — csproj with
  `IsAotCandidate=true`, `IsPackable=true`, `HasPublicApi=true`,
  project reference to `Kun.Core` only, no `<PackageReference>`
  entries. Add to `Kun.sln` in the `src/` solution folder.
- **AC-3**: Scaffold `tests/Kun.Testing.Tests` project — xUnit v3,
  references `Kun.Testing`. Add to `Kun.sln` in the `tests/`
  solution folder.
- **AC-4**: `KunAssertionException` + `ResultAssertions` — fluent
  extensions on `Result` and `Result<T>`:
  - `ShouldBeSuccess(this Result)` / `ShouldBeSuccess(this Result<T>) -> T`
    (returns the value so callers can chain further checks)
  - `ShouldBeFailure(this Result|Result<T>) -> Error`
  - `ShouldHaveErrorCode(this Result|Result<T>, string code) -> Error`
  - `ShouldHaveErrorType(this Result|Result<T>, ErrorType type) -> Error`
  All helpers throw `KunAssertionException` with a precise message
  (actual vs expected) on mismatch. XML-documented.
- **AC-5**: `DomainEventAssertions` — fluent extensions on
  `IAggregateRoot`:
  - `ShouldRaise<TEvent>(this IAggregateRoot) -> TEvent` (expects
    exactly one `TEvent`; throws with count if 0 or >1)
  - `ShouldRaiseExactly<TEvent>(this IAggregateRoot, int count) -> IReadOnlyList<TEvent>`
  - `ShouldNotRaise<TEvent>(this IAggregateRoot)`
  Use `OfType<TEvent>()` + LINQ (compile-time safe, AOT-friendly —
  no reflection).
- **AC-6**: `tests/Kun.Testing.Tests` — cover every helper: success
  path + a negative case per helper that asserts the
  `KunAssertionException` message contains both actual and expected
  info. Target ≥20 tests.
- **AC-7**: Smoke-adopt in `tests/Kun.Core.Tests` — convert 3–5
  existing assertions in `Results/ResultTests.cs` or
  `Results/ResultExtensionsTests.cs` to `Kun.Testing` helpers. This
  proves the dependency shape works across the repo before other
  workers pull it in. **Do not** mass-convert — only a handful, as a
  smoke.
- **AC-8**: `src/Kun.Testing/AGENTS.md` + `tests/Kun.Testing.Tests/AGENTS.md`
  + `CHANGELOG.md [Unreleased] ### Added` entry + PublicAPI.Shipped
  and .Unshipped seeded (new module — Shipped empty, Unshipped has
  every new public symbol).

## Commit plan

| # | Commit subject | AC | Notes |
|---|----------------|----|-------|
| 1 | `docs(testing): ADR-012 Kun.Testing module` | AC-1 | ADR-first for new top-level module |
| 2 | `feat(testing): scaffold Kun.Testing project + sln wiring` | AC-2, AC-3 | both csprojs + sln entries in one commit |
| 3 | `feat(testing): KunAssertionException + ResultAssertions` | AC-4 | PublicAPI.Unshipped updated in-commit |
| 4 | `feat(testing): DomainEventAssertions (ShouldRaise/ShouldNotRaise)` | AC-5 | PublicAPI.Unshipped updated in-commit |
| 5 | `test(testing): Kun.Testing.Tests cover all assertions` | AC-6 | xUnit v3 + FluentAssertions on the assertion messages |
| 6 | `test(core): smoke-adopt Kun.Testing in Kun.Core.Tests` | AC-7 | 3–5 call-sites only |
| 7 | `docs(testing): AGENTS.md + CHANGELOG for Kun.Testing` | AC-8 | wrap-up |

Optional 8th commit only if sln add or PublicAPI format sweeps surface issues — otherwise omit.

## AOT / build gates that must stay green

- [ ] `dotnet format` on touched projects
- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0/0
- [ ] `dotnet test tests/Kun.Testing.Tests -c Release --no-build` green
- [ ] `dotnet test tests/Kun.Core.Tests -c Release --no-build` still 85+ green (smoke adoption)
- [ ] `dotnet publish src/Kun.Testing -c Release -r linux-x64 -p:PublishAot=true /warnaserror` ILC 0 warning
- [ ] `PublicAPI.Shipped.txt` (empty) + `PublicAPI.Unshipped.txt` (every new public symbol) present
- [ ] `CHANGELOG.md [Unreleased]` has "### Added — Kun.Testing module"
- [ ] Zero non-`Kun.Core` `<PackageReference>` in `Kun.Testing.csproj`

## Progress log

- 2026-04-24 90ee2bc KN-SELF-001 closed (planner prior cycle)
- 2026-04-24 49b6918 planner — pick KN-SELF-002, write active-task.md
- 2026-04-24 f18f44b AC-1 — ADR-012 accepted
- 2026-04-24 268b3c3 AC-2+AC-3 — scaffold src/Kun.Testing + tests/Kun.Testing.Tests + sln wiring
- 2026-04-24 dad3b13 AC-4 — KunAssertionException + ResultAssertions
- 2026-04-24 a300146 AC-5 — DomainEventAssertions (ShouldRaise/ShouldRaiseExactly/ShouldNotRaise)
- 2026-04-24 a36cec6 AC-6 — 30 new tests cover every helper pass-through + mismatch-message shape
- 2026-04-24 687eba1 AC-7 — smoke-adopt Kun.Testing at 4 call sites in Kun.Core.Tests (85 still green)
- 2026-04-24 aa4b4d7 AC-8 — src/Kun.Testing/AGENTS.md + tests/Kun.Testing.Tests/AGENTS.md + CHANGELOG

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-002 in `BACKLOG.md` to `## Done`.
3. `echo "KN-SELF-002 done (<SHA>) — Kun.Testing module + fluent helpers landed." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-002_done.txt`
4. Planner next pick: KN-SELF-003 (Kun.Observability trace enricher, BACKLOG P1).
