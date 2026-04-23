<!-- Owner: claude-kun (framework self-managed) -->
<!-- Created: 2026-04-24 -->

# Kun Framework Self-Managed BACKLOG

This file is the **framework-driven** queue owned by `claude-kun`. Main
controller does **not** author items here — only the 角色1 (Planner) hat
worn by kun himself during idle windows picks the next item.

> Business-driven (`P1-KN-xxx`) tasks arrive via
> `~/.claude/dispatch-queue/claude-kun/pending/` and **take precedence**
> over any item below. See
> `.omc/plans/kun-autonomy-protocol.md` for the full two-role protocol
> and `kun/.autonomy/README.md` for the day-to-day loop.

## Conventions

- Each item has: **Priority** (P0–P3), **Size** (S / M / L, where S = ≤5
  commits, M = 5–9 commits, L = 10+ commits), **Why**, and **AC**
  (acceptance criteria summary).
- Only **S or M** items are eligible for self-managed pickup — anything
  L-sized needs a ralplan / ADR round and stays parked.
- When an item is picked, Planner writes
  `kun/.autonomy/active-task.md`, and the BACKLOG item gains a
  `Status: in-flight — KN-SELF-xxx` line.
- When an item ships, move it to the **Done** section with final commit
  SHA and date.

---

## Queue (by priority)

### P0 — KN-SELF-001: `Result<T>` async extension methods + ADR-011

- **Size**: M (target 6–8 commits)
- **Why**: `Kun.Core.Results.Result<T>` today only exposes
  `Ok` / `Fail` / `EnsureSuccess`. Every downstream handler that composes
  async operations (`neuroagent-csharp` Edge / Platform are full of
  `var r = await Foo(); if (r.IsFailure) return r.Error; var s = await Bar(r.Value); …`)
  reinvents `Map` / `Bind` / `Tap` / `Ensure` ad-hoc, often without
  honoring the short-circuit semantics `Result<T>` intends. This drift
  is exactly what ADR-010 warned about at the cookie-session layer —
  fix it at the core before more consumers copy the pattern.
- **AC**:
  1. ADR-011 accepted — naming (`MapAsync` / `BindAsync`, `ThenAsync`
     vs `BindAsync` decision), `Task<Result<T>>` vs
     `ValueTask<Result<T>>` policy, cancellation-token propagation,
     AOT safety.
  2. Sync extensions: `Map`, `Bind`, `Match`, `Tap`, `Ensure` on both
     `Result` and `Result<T>`.
  3. Async extensions: `MapAsync`, `BindAsync`, `TapAsync`,
     `EnsureAsync` accepting `Task<Result<T>>` / `ValueTask<Result<T>>`
     sources.
  4. `Kun.Core.Tests` coverage — success path, failure short-circuit,
     cancellation-token forwarding, both `Task` and `ValueTask`
     overloads.
  5. `PublicAPI.Unshipped.txt` and `CHANGELOG.md [Unreleased]` updated.
  6. `Kun.Core/Results/AGENTS.md` updated with usage examples.
  7. AOT publish of `Kun.Core` / `Kun.Mediator` still 0-warning under
     `/warnaserror`.

### P1 — KN-SELF-002: `Kun.Testing` fluent assertion helpers

- **Size**: M (target 7–9 commits)
- **Why**: Every `tests/Kun.*.Tests` project re-invents the same trio:
  `Assert.True(result.IsSuccess); Assert.Equal(expectedErrorCode,
  result.Error.Code);` plus domain-event collection asserts plus Outbox
  message shape asserts. `neuroagent-csharp` tests repeat the pattern.
  Provide a `Kun.Testing` helper assembly with fluent extensions
  (`result.ShouldBeSuccess()`, `aggregate.ShouldRaise<TEvent>()`,
  `outbox.ShouldContain("order.placed.v1")`).
- **AC**: separate `src/Kun.Testing` + `tests/Kun.Testing.Tests`
  project, AOT-candidate (no reflection), consumed by at least one
  `tests/Kun.*.Tests` project as a smoke.
- **Depends on**: KN-SELF-001 (async extensions reduce the need for
  some helpers; decide final surface after 001 lands).

### P1 — KN-SELF-003: `Kun.Observability` trace enricher (tenant/user/outbox)

- **Size**: M (target 6–8 commits)
- **Why**: `KunActivitySources` registers four sources but nothing
  auto-injects `tenant_id` / `user_id` / `outbox.message_id` as span
  tags. Downstream operators have to correlate Aspire dashboard spans
  back to logs by hand. Add a `KunActivityEnricher` that reads
  `ITenantContext` + `ICurrentUser` + the current Outbox envelope (when
  present) and tags the active span.
- **AC**: enricher in `Kun.Observability`, opt-in via
  `AddKunObservability().WithEnricher()`, OTel exporter test asserts
  tags are present, Aspire sample shows them in Dashboard.

### P2 — KN-SELF-004: `Kun.Dapper` AOT intent cleanup

- **Size**: S (target ≤3 commits)
- **Why**: AOT audit 2026-04-23 flagged `Kun.Dapper` YELLOW — no
  `<IsAotCandidate>` declaration. Set it to `false` explicitly and
  annotate `AGENTS.md` that per-query AOT is `Dapper.AOT` source
  generator's job, not `Kun.Dapper` itself.
- **AC**: csproj flag explicit, AGENTS note added, audit report TL;DR
  updated, CI AOT matrix unchanged.

### P2 — KN-SELF-005: Naming-consistency sweep on `IAuthenticatedUser`

- **Size**: S (target 3–5 commits)
- **Why**: ADR-010 introduced `IAuthenticatedUser` alongside
  `ICurrentUser`. The two must not drift in future edits —
  add compile-time tests / analyzer-like checks (or at minimum an ADR
  § on when to use which) to prevent a third adapter from being
  invented next sprint.
- **AC**: either an `.editorconfig` rule, an `ArchUnitNET` (or
  hand-rolled reflection-at-test-time) rule, or a doc
  `docs/architecture/when-to-use-currentuser.md`.

### P3 — KN-SELF-006: `Kun.Saga.EntityFrameworkCore` split (long-parked)

- **Size**: L (≥10 commits, breaking change for consumers)
- **Why**: AOT audit 2026-04-23 RED. Already tracked as
  `P2-KN-Saga-EF-Split`. Parked from self-managed queue — needs
  consumer-side ralplan coordination with `neuroagent-csharp`.
- **Blocked by**: business-layer Saga callers stabilizing.

---

## Done

<!-- append completed items here as "KN-SELF-xxx — <title> — <commit SHA> — <date>" -->
