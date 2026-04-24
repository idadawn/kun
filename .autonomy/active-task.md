---
id: KN-SELF-014
title: Kun.Testing.EntityFrameworkCore Outbox assertion helpers (ADR-015 §F3)
status: in-flight
planner_commit: <fill after re-pick commit>
executor_head: <fill after first executor commit>
estimated_commits: 4
started: 2026-04-24
rescope_note: |
  Initial 2026-04-24 pick (c3ad04d) was "docs/guides/outbox-and-integration-events.md"
  — a guide task analogous to KN-SELF-013's validation guide. User re-directed to
  pick from the three candidates listed at KN-SELF-013 session close:
  (1) ADR-015 §F3 Kun.Testing.EF Outbox helpers — M
  (2) ADR-013 §F2 metric dims from baggage — M+ (skip: no metrics pipeline yet)
  (3) ADR-013 §F3 Aspire dashboard demo — S-M
  ADR-015 §F3 has the highest ROI — directly unblocks consumer tests of
  Outbox-atomicity scenarios. Re-pick.
---

# KN-SELF-014 — `Kun.Testing.EntityFrameworkCore` Outbox assertion helpers

## Why

ADR-015 §F3 called out `Kun.Testing.EntityFrameworkCore` as a
deferred follow-up: a testing-side sibling of `Kun.Testing`
that ships assertions for EF-persisted Outbox state. Today a
consumer writing "does my command handler stage the right
`OutboxMessage` inside its transaction?" test has to:

- Open their EF `DbContext`.
- Pull `dbContext.Set<OutboxMessage>().ToList()` manually.
- Iterate, match by `EventTypeId` string, assert count / payload
  shape by hand.
- Re-write the same boilerplate at every test site.

`Kun.Testing` already ships `ResultAssertions` and
`DomainEventAssertions` for AOT-candidate scopes. The Outbox
side belongs in a sibling module that takes EF Core as a
dependency (non-AOT — matches `Kun.EntityFrameworkCore`
`IsAotCandidate=false`) and lets consumers write:

```csharp
dbContext.ShouldHaveOutboxMessage("order.placed.v1");
dbContext.ShouldHaveExactlyOneOutboxMessage("order.placed.v1");
dbContext.ShouldNotHaveOutboxMessage("payment.refunded.v1");
```

Four commits, clean M-size scope, directly closes ADR-015 §F3.

## Specs to read first (non-negotiable)

Required by `kun/CLAUDE.md` §Specs-first. Read before coding:

1. `kun/CLAUDE.md` — §Outbox + §Architectural constraints (AOT
   per-module + four-layer flow).
2. `kun/docs/architecture/ADR-012-Kun-Testing.md` — parent-
   module design decisions (why `Kun.Testing` exists, what
   shape its helpers take).
3. `kun/docs/architecture/ADR-015-Outbox-pattern.md` — especially
   §F3 deferred list.
4. `kun/src/Kun.Testing/AGENTS.md` — sibling-module conventions.
5. `kun/src/Kun.Testing/Results/ResultAssertions.cs` — shape
   of helper methods (extension-method, `KunAssertionException`
   with precise messages).
6. `kun/src/Kun.Testing/KunAssertionException.cs` — framework-
   agnostic assertion-failure type.
7. `kun/src/Kun.EventBus/Outbox/OutboxMessage.cs` — the type
   under test (properties: `Id`, `EventTypeId`, `Payload`,
   `Status`, `TenantId`, `TraceParent`, `TraceState`, etc.).
8. `kun/src/Kun.EntityFrameworkCore/*.csproj` —
   `IsAotCandidate=false` + package reference pattern to copy.
9. `kun/Directory.Packages.props` — existing
   `Microsoft.EntityFrameworkCore` pin.
10. `kun/Kun.sln` — solution add pattern for a new module +
    test project.
11. `kun/tests/Kun.Testing.Tests/Kun.Testing.Tests.csproj` — test-
    project scaffold pattern.

## Acceptance criteria

- **AC-1**: New module `src/Kun.Testing.EntityFrameworkCore/`:
  - `Kun.Testing.EntityFrameworkCore.csproj` with
    `IsAotCandidate=false`, `IsPackable=true`,
    `HasPublicApi=true`, `IsTrimmable=true` (via
    `Directory.Build.props` switch), `<Description>` mirroring
    Kun.Testing but noting the EF Core dependency.
  - ProjectReferences: `Kun.Testing`, `Kun.EventBus`.
  - PackageReferences: `Microsoft.EntityFrameworkCore` (version
    pinned in CPM).
  - `AGENTS.md` (parent `../AGENTS.md`, brief scope note,
    cross-link to ADR-015 §F3 resolved).
  - Empty seeded `PublicAPI.Shipped.txt` + auto-populated
    `PublicAPI.Unshipped.txt` (after coding AC-2, re-run
    `dotnet format analyzers --diagnostics=RS0016` to seed).
  - Added to `Kun.sln` alongside `Kun.Testing`.

- **AC-2**: `Outbox/OutboxAssertions.cs` public static class
  with three helpers, each mirroring `ResultAssertions` shape
  (extension method, `KunAssertionException` with precise
  message, ordinal string comparison):
  - `ShouldHaveOutboxMessage(this DbContext db, string eventTypeId)`
    — succeeds if ≥1 row with matching `EventTypeId`; throws
    with observed-count + eventTypeId list on mismatch.
  - `ShouldHaveExactlyOneOutboxMessage(this DbContext db, string eventTypeId)`
    — succeeds if exactly 1 row; throws with observed-count on
    mismatch.
  - `ShouldNotHaveOutboxMessage(this DbContext db, string eventTypeId)`
    — succeeds if 0 rows; throws with observed-count on
    mismatch.
  - All three internally enumerate `db.Set<OutboxMessage>()`
    synchronously via `AsEnumerable()` — test callers use
    `DbContext.SavedChanges` + assert, not async DB round-
    trips.
  - Also ship a parallel `IEnumerable<OutboxMessage>` overload
    of each helper (`this IEnumerable<OutboxMessage>, string eventTypeId`)
    so tests that already have a snapshot collection don't
    need a DbContext.

- **AC-3**: Test project `tests/Kun.Testing.EntityFrameworkCore.Tests/`:
  - `.csproj` mirrors `Kun.Testing.Tests` shape (xUnit, test SDK,
    coverlet collector, ProjectReference to the new module).
  - Added to `Kun.sln`.
  - `AGENTS.md` parent-linked.
  - Tests covering each helper's pass and fail paths (minimum:
    ShouldHaveOutboxMessage pass + fail-no-match +
    fail-wrong-eventType; ShouldHaveExactlyOne pass + fail-
    zero + fail-two; ShouldNotHave pass + fail-one). Use a
    lightweight in-memory `IEnumerable<OutboxMessage>` fixture
    (constructed via reflection-free `OutboxMessage.Create` if
    it exists, otherwise instantiated through a small test
    helper). Avoid spinning a full `DbContext` if a plain
    collection suffices — test both helper families
    (DbContext + IEnumerable) with a minimal harness.

- **AC-4**: Documentation + CHANGELOG:
  - `CHANGELOG.md [Unreleased]` gains a new "Added" entry
    referencing KN-SELF-014 + ADR-015 §F3 resolved.
  - `docs/architecture/ADR-015-Outbox-pattern.md` §F3 bullet
    annotated "Resolved 2026-04-24 (KN-SELF-014)" with a
    one-line description of what shipped.
  - `src/Kun.EventBus/AGENTS.md` picks up a one-line cross-
    link near "Testing Requirements" section pointing at the
    new module as the canonical Outbox test helper.
  - `src/Kun.Testing/AGENTS.md` picks up a sibling-module
    note cross-linking to the EntityFrameworkCore variant.

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `feat(testing.efcore): scaffold Kun.Testing.EntityFrameworkCore module` | AC-1 |
| 2 | `feat(testing.efcore): OutboxAssertions with Have/ExactlyOne/None helpers` | AC-2 |
| 3 | `test(testing.efcore): tests project + OutboxAssertions coverage` | AC-3 |
| 4 | `docs(testing.efcore): CHANGELOG + ADR-015 §F3 resolved + cross-links` | AC-4 |

## AOT / build gates that must stay green

- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0 errors.
- [ ] `dotnet test tests/Kun.Testing.EntityFrameworkCore.Tests -c Release --no-build`
      green.
- [ ] `dotnet test Kun.sln -c Release --no-build` — no regression
      in `Kun.Testing.Tests` / `Kun.Core.Tests` / `Kun.Validation.Tests`
      (etc.) from the new csproj landing.
- [ ] `Kun.Testing.EntityFrameworkCore` **not** AOT-published
      (matches `Kun.EntityFrameworkCore` `IsAotCandidate=false`);
      trim-warning analyzer is enabled per `Directory.Build.props`.
- [ ] Kun.Testing (parent) AOT publish remains 0-warning — the
      new sibling module shouldn't leak any non-AOT reference
      back into Kun.Testing itself. Sanity check by re-publishing
      Kun.Testing.
- [ ] `PublicAPI.Unshipped.txt` seeded for the new module
      (regenerated via `dotnet format analyzers
      --diagnostics=RS0016`).
- [ ] `CHANGELOG.md [Unreleased]` updated.

## Out of scope

- Outbox **factory** helpers (pre-populating rows in Arrange
  phase). If an AC-3 test needs a factory, write a *test-
  local* one — do not promote to public API.
- Async DbContext query variants (`ShouldHaveOutboxMessageAsync`).
  Synchronous enumeration is sufficient for unit tests that
  just saved the context.
- Dead-letter assertions (ADR-015 §F1 — dead-letter surface
  not shipped yet, no state to assert on).
- DbContext extension overloads beyond the three primary
  helpers (e.g. `ShouldHaveOutboxMessagesOfStatus`) — keep the
  first cut tight; add on demand.

## Progress log

- 2026-04-24 997c64b KN-SELF-013 closed + archived.
- 2026-04-24 c3ad04d first KN-SELF-014 pick (Outbox guide) —
  rescoped per user direction, see rescope_note.

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-014 to BACKLOG `## Done`.
3. `echo "KN-SELF-014 done (<SHA>) — Kun.Testing.EntityFrameworkCore
   module shipped with OutboxAssertions (Have/ExactlyOne/None
   via DbContext or IEnumerable); ADR-015 §F3 resolved." >
   /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-014_done.txt`
4. Planner next pick candidates: ADR-013 §F3 Aspire dashboard
   demo (S-M), outbox-and-integration-events consumer guide
   (S, now justifiable as a sibling to the helpers that just
   shipped), or fresh pain point found while coding.
