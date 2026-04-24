---
id: KN-SELF-016
title: Complete Kun.Testing.EntityFrameworkCore tests + docs (ADR-015 §F3)
status: in-flight
planner_commit: <fill after commit>
executor_head: <fill after first executor commit>
estimated_commits: 2
started: 2026-04-24
---

# KN-SELF-016 — complete `Kun.Testing.EntityFrameworkCore` tests + docs

## Why

During the KN-SELF-014 rescope sequence, two commits for the
`Kun.Testing.EntityFrameworkCore` module landed on develop
and remain orphaned:

- `829ea5f feat(testing.efcore): scaffold Kun.Testing.EntityFrameworkCore module`
  — csproj (`IsAotCandidate=false`, EF Core + Kun.EventBus +
  Kun.Testing deps), AGENTS.md, PublicAPI stubs, Kun.sln
  wire-up.
- `9999f63 feat(testing.efcore): OutboxAssertions with Have/ExactlyOne/None helpers`
  — 6 extension methods (3 helpers × 2 overloads:
  `IEnumerable<OutboxMessage>` + `DbContext`) + populated
  `PublicAPI.Unshipped.txt`.

Both build 0-warning but have no consumer and no test coverage.
The `CHANGELOG.md [Unreleased]` "Added" entry and the
`docs/architecture/ADR-015-Outbox-pattern.md` §F3 resolution
marker are also missing. KN-SELF-016 ships the remaining
pieces so the module is consumer-ready and ADR-015 §F3 closes.

## Specs to read first (non-negotiable)

1. `kun/CLAUDE.md` §Specs-first + §Architectural constraints.
2. `kun/src/Kun.Testing.EntityFrameworkCore/AGENTS.md` — the
   scoping conventions laid down at 829ea5f (non-AOT, throw
   `KunAssertionException`, synchronous enumeration, mismatch-
   message shape).
3. `kun/src/Kun.Testing.EntityFrameworkCore/Outbox/OutboxAssertions.cs`
   — the public surface to test. Grep for each helper name +
   message shape so test assertions anchor on the actual
   output, not guesses.
4. `kun/docs/architecture/ADR-015-Outbox-pattern.md` §F3
   (deferred follow-up to annotate resolved).
5. `kun/tests/Kun.Testing.Tests/` — sibling test project
   layout pattern (csproj, xUnit.v3 + FluentAssertions, test
   file per assertion class).
6. `kun/Directory.Packages.props` — `Microsoft.EntityFrameworkCore.InMemory`
   is pinned (line 35) for the DbContext overload tests.

## Acceptance criteria

- **AC-1**: Test project `tests/Kun.Testing.EntityFrameworkCore.Tests/`:
  - `.csproj` mirrors `tests/Kun.Testing.Tests/Kun.Testing.Tests.csproj`
    (xUnit.v3, test SDK, vs-runner, FluentAssertions,
    coverlet). ProjectReference to
    `..\..\src\Kun.Testing.EntityFrameworkCore\…`.
    PackageReference `Microsoft.EntityFrameworkCore.InMemory`
    for the DbContext overload fixture.
  - `AGENTS.md` parent-linked, brief (testing scope lives
    under the module's own AGENTS).
  - Added to `Kun.sln` (Project declaration + configuration
    entries for Debug/Release × Any CPU/x64/x86 + NestedProjects
    under the tests folder `{22222222-…}`).
  - `OutboxAssertionsTests.cs` covering every helper's pass
    + fail paths for both overload families. Minimum coverage:
    - `ShouldHaveOutboxMessage(IEnumerable)` — match present /
      match absent / multiple matches (all count as success for
      "at least one")
    - `ShouldHaveExactlyOneOutboxMessage(IEnumerable)` — exactly
      one / zero / two
    - `ShouldNotHaveOutboxMessage(IEnumerable)` — zero / present
    - DbContext overload parity — one test per helper confirming
      the convenience wrapper delegates correctly (uses
      `UseInMemoryDatabase("name")` + `Add(outboxMessage)` + `SaveChanges`).
    - Mismatch-message shape: at least one negative-path test
      per helper anchors on helper name + expected eventTypeId +
      observed count (per the module's AGENTS.md
      §mismatch-message contract).

- **AC-2**: Documentation + CHANGELOG:
  - `CHANGELOG.md [Unreleased]` gains a new "Added" entry
    above the security entry, referencing KN-SELF-016 +
    ADR-015 §F3 resolved and listing the 6 public helpers.
  - `docs/architecture/ADR-015-Outbox-pattern.md` §F3 bullet
    (if one exists — otherwise in the `## Follow-ups` list)
    annotated "Resolved 2026-04-24 (KN-SELF-016)" with a
    short description of what shipped.
  - `src/Kun.EventBus/AGENTS.md` picks up a one-line cross-
    link near "Testing Requirements" pointing at
    `src/Kun.Testing.EntityFrameworkCore` as the canonical
    Outbox test-helper module.
  - `src/Kun.Testing/AGENTS.md` picks up a sibling-module
    note cross-linking to `Kun.Testing.EntityFrameworkCore`
    (already partially done by module's own AGENTS.md at
    829ea5f — just the reverse direction).

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `test(testing.efcore): tests project + OutboxAssertions coverage` | AC-1 |
| 2 | `docs(testing.efcore): CHANGELOG + ADR-015 §F3 resolved + cross-links` | AC-2 |

## AOT / build gates

- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0 errors.
- [ ] `dotnet test tests/Kun.Testing.EntityFrameworkCore.Tests -c Release --no-build`
      green (all new tests pass).
- [ ] `dotnet test Kun.sln -c Release --no-build` — no regressions
      in sibling test projects.
- [ ] No AOT publish — `Kun.Testing.EntityFrameworkCore` is
      deliberately `IsAotCandidate=false` (matches
      `Kun.EntityFrameworkCore`).
- [ ] `PublicAPI.Unshipped.txt` unchanged (no new public API
      in this task — only tests + docs).
- [ ] `CHANGELOG.md [Unreleased]` updated.

## Out of scope

- Outbox **factory** helpers (pre-populating rows in Arrange
  phase, e.g. `OutboxMessageFactory.Pending<T>(...)`).
  Tests needing fixtures can hand-roll them locally; do not
  promote to public API.
- Async DbContext helper variants
  (`ShouldHaveOutboxMessageAsync`).
- Dead-letter assertions (ADR-015 §F1, not yet shipped).

## Progress log

- 2026-04-24 ce910a4 KN-SELF-015 AC-1 shipped; closeout
  <pending>. Unblocked solution build → KN-SELF-016 can now
  verify.

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-016 to BACKLOG `## Done`; remove the P3
   follow-up entry.
3. `echo "KN-SELF-016 done (<SHA>) — Kun.Testing.EntityFrameworkCore
   tests + docs shipped; ADR-015 §F3 fully closed." >
   /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-016_done.txt`
4. Planner next pick candidates: ADR-013 §F2 metric dimensions
   (still blocked on no-metrics-pipeline — needs ADR or ralplan),
   outbox-and-integration-events consumer guide (S, now
   justifiable as a sibling to the just-closed helpers),
   or fresh pain point.
