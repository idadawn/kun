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

<!-- KN-SELF-001 moved to ## Done on 2026-04-24 (executor head 48937db) -->


### P1 — KN-SELF-002: `Kun.Testing` fluent assertion helpers

- **Status**: in-flight — see `kun/.autonomy/active-task.md`
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

- **KN-SELF-001** — `Result<T>` async extension methods + ADR-011 —
  executor head `48937db` (ADR-011 + 6 code/doc commits) — 2026-04-24.
  Seven ACs shipped: ADR accepted, sync Map/Bind/Tap/Ensure/Match on
  `Result(<T>)`, async on `Task<Result<T>>` and `ValueTask<Result<T>>`
  sources, 41 new tests (CT forwarding + Task/ValueTask parity),
  CHANGELOG + PublicAPI.Unshipped + Results/AGENTS.md usage examples.
  Kun.Core ILC 0-warning; Kun.Mediator regression ILC 0-warning.
  Deliberate deviation from the ADR sketch: async-delegate CT is
  required (not `= default`) — RS0026 rationale recorded in the
  amended ADR §CancellationToken semantics.
