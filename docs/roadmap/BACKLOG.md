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


<!-- KN-SELF-002 moved to ## Done on 2026-04-24 (executor head aa4b4d7) -->


<!-- KN-SELF-003 moved to ## Done on 2026-04-24 (executor head 12cf37c) -->


<!-- KN-SELF-004 moved to ## Done on 2026-04-24 (executor head 674a323) -->


<!-- KN-SELF-005 moved to ## Done on 2026-04-24 (executor head d5d9b68) -->

<!-- KN-SELF-006 moved to ## Done on 2026-04-24 (executor head c02d190) -->


### P2 — KN-SELF-007: `Kun.Validation` Result-returning pipeline behavior (ADR-011 consumer)

- **Status**: in-flight — see `kun/.autonomy/active-task.md`
- **Size**: M (target 5 commits)
- **Why**: The existing `ValidationPipelineBehavior` throws
  `ValidationException` on validator failure. Its own XML doc flags
  "Result-based semantics shipped as a follow-up" — consumers who
  return `Result<T>` from handlers want validation failures to land
  as `Result<T>.Fail(Error.Validation(...))` rather than throwing.
  KN-SELF-001 made the `Result<T>` composition ergonomic; this is
  the missing validation-stage enablement of the
  "business-failures-use-Result-not-exceptions" rule from
  `kun/CLAUDE.md` §Architectural constraints.
- **AC**: ADR-014, `ValidationResultPipelineBehavior<TRequest, TValue>`,
  `AddValidationResultBehavior<TRequest, TValue>()` extension,
  test coverage for the 5 canonical scenarios, AGENTS + CHANGELOG.

### P3 — KN-SELF-006-legacy: `Kun.Saga.EntityFrameworkCore` split (long-parked)

- **Size**: L (≥10 commits, breaking change for consumers)
- **Why**: AOT audit 2026-04-23 RED. Already tracked as
  `P2-KN-Saga-EF-Split`. Parked from self-managed queue — needs
  consumer-side ralplan coordination with `neuroagent-csharp`.
- **Blocked by**: business-layer Saga callers stabilizing.

---

## Done

- **KN-SELF-006** — `Kun.Observability` log-record enricher (ADR-013 §F1) —
  executor head `c02d190` (ADR amendment + 4 code/test/doc commits) —
  2026-04-24. Five ACs shipped, hit the 5-commit estimate: ADR-013
  amended with a §F1 resolution section (chose amendment over new
  ADR — design is a direct mirror of the span enricher),
  `KunBaggageLogRecordProcessor` (internal `BaseProcessor<LogRecord>`,
  OnEnd hook, idempotent — never overwrites a caller-set attribute,
  no-op without `Activity.Current`), `EnrichLogsFromBaggage` opt-in
  flag + `AddKunObservability` logging-pipeline wiring, 4 new
  InMemoryExporter tests (happy path + flag-off + caller-attribute-
  preserved + no-Activity paths), AGENTS + CHANGELOG. Flipping both
  `EnrichActivitiesFromBaggage` and `EnrichLogsFromBaggage` now
  closes the spans↔logs correlation loop — same `tenant.id=…`
  filter works uniformly across Aspire trace UI and log view.
  Kun.Observability ILC 0-warning; sln build 0 errors; 15/15 tests
  pass (11 existing + 4 new).
- **KN-SELF-005** — `IAuthenticatedUser` vs `ICurrentUser` naming-
  consistency sweep — executor head `d5d9b68` (2 doc commits) —
  2026-04-24. Two ACs shipped: new
  `docs/architecture/when-to-use-currentuser.md` guidance doc with
  decision table + extension rules + anti-patterns; XML
  `<remarks>` cross-links added to `ICurrentUser.cs` and
  `IAuthenticatedUser.cs` so IDEs surface the guidance at the code
  site. BACKLOG's three alternatives (editorconfig rule / reflection-
  at-test-time rule / doc) reduced to doc-only — other two needed a
  maintained allow-list with the same drift shape we were trying to
  close.
- **KN-SELF-004** — `Kun.Dapper` AOT intent cleanup —
  executor head `674a323` (2 doc commits) — 2026-04-24.
  Two ACs shipped: `src/Kun.Dapper/AGENTS.md` gains the AOT-intent
  bullet pinning down why `IsAotCandidate=false` is deliberate and
  warning future agents not to flip; `docs/architecture/AOT-audit-2026-04-23.md`
  gains four resolution markers (TL;DR, matrix row, §YELLOW
  detail, §后续建议) turning the prior YELLOW into a dated
  green resolved. No csproj changes — the flag had already
  shipped in a prior cycle; this task closed the documentation gap.
- **KN-SELF-003** — `Kun.Observability` baggage activity enricher + ADR-013 —
  executor head `12cf37c` (ADR-013 + 5 code/test/doc commits) — 2026-04-24.
  Six ACs shipped (final plan hit 6 commits, 1 under estimate): ADR
  accepted, `KunBaggageKeys` + internal `KunBaggageActivityProcessor`
  (OnEnd hook, AsyncLocal-safe baggage-to-tag mirror for Kun-owned
  ActivitySources only), `EnrichActivitiesFromBaggage` opt-in flag
  + `AddKunObservability` wiring, 7 new InMemoryExporter tests
  (manual TracerProviderBuilder + hosted ServiceProvider paths),
  AGENTS + CHANGELOG + ADR §OnEnd amendment. Kun.Observability ILC
  0-warning; sln build 0 errors. HasPublicApi flip intentionally
  deferred as a future framework-wide task. Out-of-scope tracked
  as ADR-013 F1–F3 (log-record enricher, metric dimensions, Aspire
  dashboard demo).
- **KN-SELF-002** — `Kun.Testing` fluent assertion helpers + ADR-012 —
  executor head `aa4b4d7` (ADR-012 + 6 code/test/doc commits) — 2026-04-24.
  Seven ACs shipped: ADR accepted, src/Kun.Testing module scaffolded
  (AOT-candidate, packable, zero third-party runtime deps) + wired
  into Kun.sln, `KunAssertionException`, `ResultAssertions` (sync
  + generic variants), `DomainEventAssertions` (`ShouldRaise` /
  `ShouldRaiseExactly` / `ShouldNotRaise` via LINQ `OfType<T>()`),
  30 new tests covering every helper's pass-through and
  exception-message shape, 4-site smoke adoption in Kun.Core.Tests
  keeping 85 existing tests green, src/Kun.Testing/AGENTS.md +
  tests/Kun.Testing.Tests/AGENTS.md + CHANGELOG [Unreleased] entry.
  Kun.Testing ILC 0-warning; sln build 0 errors.
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
