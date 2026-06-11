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


<!-- KN-SELF-007 moved to ## Done on 2026-04-24 (executor head 2d7cc50) -->


<!-- KN-SELF-008 moved to ## Done on 2026-04-24 (executor head af4007c) -->


<!-- KN-SELF-009 moved to ## Done on 2026-04-24 (executor head 5672525) -->


<!-- KN-SELF-010 moved to ## Done on 2026-04-24 (executor head 2eb3b17) -->


<!-- KN-SELF-011 moved to ## Done on 2026-04-24 (executor head 74f21c1) -->


<!-- KN-SELF-012 moved to ## Done on 2026-04-24 (executor head 4554c8d) -->


<!-- KN-SELF-013 moved to ## Done on 2026-04-24 (executor head e3e9e45) -->


<!-- KN-SELF-014-rescoped moved to ## Done on 2026-04-24 (executor head 25a3604) -->


<!-- KN-SELF-015 (NU1902 / GHSA-g94r-2vxg-569j) moved to ## Done on 2026-04-24 -->

<!-- KN-SELF-016 moved to ## Done on 2026-04-24 (executor head 5ceffdc + this closeout) -->


<!-- KN-SELF-017 moved to ## Done on 2026-04-24 (executor head e9ad7b9 + this closeout) -->


### P2 — KN-SELF-018: end-to-end `orchestrating-sagas.md` consumer guide

- **Status**: done — `docs/guides/orchestrating-sagas.md` 已落地（2026-06-12 核对）
- **Size**: S (target 2 commits).
- **Why**: Validation (KN-SELF-013), Outbox (KN-SELF-017)
  each have a consumer-facing walkthrough under
  `docs/guides/`. Saga has only `why-saga.md` (primer) +
  `src/Kun.Saga/AGENTS.md` (agent guardrails) + abstraction
  interfaces. A downstream engineer writing their first
  `ISaga<TContext>` + ordered steps + compensation still
  has to triangulate. Close the same gap shape for the
  Saga subsystem.
- **AC**: new `docs/guides/orchestrating-sagas.md` with
  orchestration-vs-choreography framing, the 4 interface
  members engineers implement (ISaga Name/Steps/DefaultStepTimeout
  + ISagaStep Name/Timeout/ExecuteAsync/CompensateAsync),
  atomic-start boundary, context JsonTypeInfo registration,
  a 3-step OrderCreationSaga worked example, compensation
  + timeout lifecycle, idempotency rules, four anti-patterns
  (cross-step mutable state / compensation throwing on
  nothing-to-undo / CLR-name step IDs / choreography
  infiltration), edges footer (P2-KN-Saga-EF-Split +
  testing-helpers-not-yet), see-also; `src/Kun.Saga/AGENTS.md`
  cross-link near Purpose.
- **Size**: S (target 2 commits: tests project + CHANGELOG /
  ADR-015 §F3 resolution marker).
- **Why**: KN-SELF-014 was rescoped mid-execution. The
  `src/Kun.Testing.EntityFrameworkCore/` module (csproj, AGENTS,
  `OutboxAssertions` class, PublicAPI seed) is already on
  develop and building clean, but has no test coverage yet and
  the ADR-015 §F3 "resolved" marker + CHANGELOG entry have
  not landed.
- **AC**: `tests/Kun.Testing.EntityFrameworkCore.Tests/`
  project scaffold + xUnit.v3 + FluentAssertions + tests for
  each helper's pass and fail paths (both
  `IEnumerable<OutboxMessage>` and `DbContext` overloads;
  DbContext uses `UseInMemoryDatabase` fixture);
  `CHANGELOG.md [Unreleased]` "Added" entry; ADR-015 §F3
  bullet annotated "Resolved <date> (KN-SELF-015)";
  `Kun.EventBus/AGENTS.md` + `Kun.Testing/AGENTS.md`
  cross-links.

### P3 — KN-SELF-006-legacy: `Kun.Saga.EntityFrameworkCore` split (long-parked)

- **Size**: L (≥10 commits, breaking change for consumers)
- **Why**: AOT audit 2026-04-23 RED. Already tracked as
  `P2-KN-Saga-EF-Split`. Parked from self-managed queue — needs
  consumer-side ralplan coordination with `neuroagent-csharp`.
- **Blocked by**: business-layer Saga callers stabilizing.

---

## Done

- **KN-SELF-017** — end-to-end
  `docs/guides/outbox-and-integration-events.md` consumer guide
  — executor head `e9ad7b9` (1 guide commit + 1 AGENTS
  cross-link / closeout commit) — 2026-04-24. Two ACs shipped:
  new `docs/guides/outbox-and-integration-events.md` (~260
  lines, Chinese prose + English code, matches
  `validating-commands.md` density) walks a downstream
  engineer from "what's the atomicity boundary?" through the
  publish + consume worked examples, the MessageContext-
  keyed idempotency pattern, the W3C trace-context-vs-ADR-013-
  baggage distinction, four anti-patterns (direct broker
  bypass, AQN event IDs, non-idempotent consumer,
  MessageContext mutation), an edges footer noting ADR-015 §F1
  / §F2 open follow-ups, and a see-also to KN-SELF-016's
  `OutboxAssertions` for the test side; every type / method /
  extension name verified against HEAD before commit.
  `src/Kun.EventBus/AGENTS.md` picks up a one-line consumer-
  entry-point cross-link near Purpose pointing at the guide.
  Doc-only; closes the Outbox-story gap in `docs/guides/`
  that mirrored the KN-SELF-013 validation gap. Together
  with KN-SELF-011 (ADR) + KN-SELF-014 (scaffold) +
  KN-SELF-016 (tests + §F3 resolution), the Outbox story is
  fully consumer-facing-documented.

- **KN-SELF-016** — complete
  `Kun.Testing.EntityFrameworkCore` tests + docs (ADR-015 §F3
  closed) — executor head `5ceffdc` (1 test commit by kun + 1
  doc closeout commit) — 2026-04-24. Two ACs shipped: new
  `tests/Kun.Testing.EntityFrameworkCore.Tests/` project
  (xUnit.v3 + FluentAssertions + coverlet +
  `Microsoft.EntityFrameworkCore.InMemory`) wired into
  `Kun.sln` under tests folder, with
  `OutboxAssertionsTests.cs` covering every helper × overload
  × pass/fail path (11 tests; `UseInMemoryDatabase(Guid.NewGuid())`
  per test for hermetic `DbContext`-overload fixtures; all
  698ms). CHANGELOG [Unreleased] "Added" entry + ADR-015 §F3
  bullet annotated "Resolved 2026-04-24 (KN-SELF-014 +
  KN-SELF-016)" + `Kun.EventBus/AGENTS.md` §Testing
  Requirements cross-link to `OutboxAssertions` +
  `Kun.Testing/AGENTS.md` sibling-module guidance bullet.
  Together with KN-SELF-014's scaffold (`829ea5f`) +
  `OutboxAssertions` class (`9999f63`), the module is now
  fully consumer-ready and ADR-015 §F3 is closed as a
  deferred follow-up.

- **KN-SELF-015** — resolve NU1902 advisory on
  `OpenTelemetry.Api 1.12.0`
  ([GHSA-g94r-2vxg-569j](https://github.com/advisories/GHSA-g94r-2vxg-569j),
  moderate severity) — executor head `ce910a4` (1 fix commit
  by main controller + 1 CHANGELOG / closeout commit by
  kun) — 2026-04-24. Two ACs shipped: AC-1 bumped the
  `Directory.Packages.props` OpenTelemetry group so
  `OpenTelemetry.Api` is explicitly pinned to `1.15.3`
  (patched) with companion pins on `OpenTelemetry` /
  `OpenTelemetry.Extensions.Hosting` / `OpenTelemetry.Exporter.*`
  raised to `1.12.0` for group coherence — CPM now force-
  resolves the transitive graph above the CVE line; AC-2
  added a CHANGELOG `[Unreleased]` "Security" entry
  documenting advisory URL, fix commit, patched version,
  and downstream-consumer guidance (align `OpenTelemetry.Api`
  on 1.15.3). Gate: `dotnet build Kun.sln -c Release
  -p:TreatWarningsAsErrors=true` now 0 errors (previously
  2 NU1902 errors on Kun.Sample.Catalog + Kun.Sample.Tests).
  Unblocked every subsequent sample / AOT verification path.

- **KN-SELF-014-rescoped** — Aspire Dashboard baggage demo
  (ADR-013 §F3) — executor head `25a3604` (2 feat commits by
  main controller + 2 planner rescope commits by kun) —
  2026-04-24. Two ACs shipped: `Kun.Sample.Catalog/Program.cs`
  swapped `Kun.Sample.Shared.AddKunObservability` for
  `Kun.Observability.Extensions.AddKunObservability` with
  `EnrichActivitiesFromBaggage=true` +
  `EnrichLogsFromBaggage=true` (`46f9715`); `/health` endpoint
  sets `KunBaggageKeys.TenantId="demo-tenant-0001"` +
  `KunBaggageKeys.UserId="demo-user-0001"` on
  `Activity.Current` so the activity enricher
  (`KunBaggageActivityProcessor`) mirrors them onto the ASP.NET
  span's tags for Aspire Dashboard trace view (`25a3604`).
  AC-3 (solution build green) blocked by a **pre-existing**
  NU1902 advisory (GHSA-g94r-2vxg-569j) against transitively-
  pulled `OpenTelemetry.Api 1.12.0` — not regressed by this
  task; Kun.Sample.Catalog + Kun.Sample.Tests both hit it
  since the NuGet advisory refresh landed mid-session. Tracked
  as P1 follow-up below. Rescope history: `c3ad04d` (outbox
  guide, rescoped) → `a0fe17e` (Kun.Testing.EF Outbox helpers,
  partial-shipped then rescoped) → `4d8b94a` + `af5d84b`
  (Aspire demo, final pick). Partial helpers from a0fe17e are
  orphaned on develop (scaffold + OutboxAssertions class are
  functional and build 0-warning; tests + CHANGELOG / ADR-015
  §F3 resolution marker deferred — see P3 follow-up above).

- **KN-SELF-013** — end-to-end `validating-commands.md` guide +
  Kun.Validation AGENTS refresh — executor head `e3e9e45`
  (1 new-guide + 1 AGENTS-refresh commit — hit the 2-commit
  estimate exactly) — 2026-04-24. Two ACs shipped: new
  `docs/guides/validating-commands.md` (~170 lines, Chinese
  prose matching sibling guides, English code blocks)
  surfacing the now-complete validation story with a 3-row
  decision tree, two worked examples (CreateOrderCommand for
  the generic `Result<TValue>` path + DeleteUserCommand for
  the non-generic `Result` path shipped by KN-SELF-012), three
  anti-patterns (double-register, throw-inside-Result-handler,
  DB-in-Validator), and a see-also footer cross-linking
  ADR-011 / ADR-014 / Kun.Validation AGENTS / Kun.Testing
  AGENTS / when-to-use-currentuser; plus a surgical refresh
  of `src/Kun.Validation/AGENTS.md` "Two pipeline behaviors
  ship" subsection (→ "Three"), new bullet describing the
  non-generic variant + its single-type-param DI overload,
  inline ADR-014 §F1 resolution marker, guide cross-link,
  front-matter "Updated: 2026-04-24" bump. All relative-link
  targets + type/extension names verified before push.
  Doc-only; build/AOT gates not applicable.

- **KN-SELF-012** — non-generic `Result` variant of
  `ValidationResultPipelineBehavior` (ADR-014 §F1) — executor
  head `4554c8d` (1 feat + 1 test + 1 doc commit — hit the
  3-commit estimate exactly) — 2026-04-24. Four ACs shipped:
  sibling class `ValidationResultPipelineBehavior<TRequest>`
  where `TRequest : IRequest<Result>` co-located with the
  generic variant in the same file (same validator loop, same
  `"; "`-joined `Error.Validation("request.invalid", …)` shape,
  returns `Result.Fail(...)` instead of
  `Result<TValue>.Fail(...)`), `AddValidationResultBehavior<TRequest>()`
  single-type-param overload on
  `KunValidationServiceCollectionExtensions`, 3 mirrored tests
  (validator-fail → `Result.Fail(request.invalid)`, multi-validator
  aggregation, handler-level `Result.Fail` preserved untouched),
  `CHANGELOG.md [Unreleased]` Added entry + `ADR-014 §F1`
  resolution marker with a note that the "doubled registration
  surface" concern was overstated. 20/20 Kun.Validation tests
  pass (17 existing + 3 new). Solution build 0 errors;
  Kun.Validation ILC 0-warning. Closes KN-SELF-007's deferred
  follow-up — void-returning commands can now participate in
  the `Result`-based railway without throwing or hand-rolling
  a catch adapter.

- **KN-SELF-011** — ADR-015 Outbox pattern design (retroactive) —
  executor head `74f21c1` (ADR + cross-link + addendum commits,
  1 over estimate because AC-2 edit needed a Read-first retry
  that split into two commits) — 2026-04-24. ADR-015 walks a
  reader through the existing Outbox design: type surface across
  `Kun.EventBus` + `Kun.EntityFrameworkCore`, atomicity boundary
  (the `SaveChangesAsync` transaction), publisher pump
  (background service + lock providers), consumer idempotency
  link (`IdempotentConsumerDecorator<T>`), W3C trace propagation
  (distinct from ADR-013 baggage), stable event IDs
  (`{bounded-context}.{name}.v{n}`), and rejected alternatives
  (direct publish / CDC / Kafka-as-ledger / per-context tables).
  Cross-linked from `kun/CLAUDE.md` §Outbox, `Kun.EventBus/AGENTS.md`,
  `docs/architecture/why-outbox.md`. F1–F3 track dead-letter
  handling, per-tenant batch priority, and
  Kun.Testing.EntityFrameworkCore Outbox helpers as future tasks.
- **KN-SELF-010** — complete HasPublicApi sweep (11 modules) —
  executor head `2eb3b17` (3 batch commits + 1 CHANGELOG
  consolidation commit — hit the 4-commit estimate exactly) —
  2026-04-24. Pilot chain KN-SELF-008 → 009 → 010 **complete**:
  every `src/Kun.*` module now flags `<HasPublicApi>true</HasPublicApi>`.
  Sanity grep `<HasPublicApi>false</HasPublicApi>` returns zero
  across `src/`. Pre-existing shape-distinguished dual-overload
  pattern on `Kun.Identity.AspNetCore.SignInHelper.SignInAsync`
  got a file-level RS0026 suppress with the same rationale as
  `Kun.Sandbox.IProcesses.RunAsync` (KN-SELF-009). From now on,
  any public-symbol change across any Kun module fails the
  analyzer gate until reviewed — consumer-facing API drift is
  closed as a structural risk.
- **KN-SELF-009** — flip `Kun.Sandbox` to `HasPublicApi=true` —
  executor head `5672525` (1 combined commit, under estimate) —
  2026-04-24. Second step of the KN-SELF-008 pilot sweep. 120
  PublicAPI.Unshipped entries auto-emitted by `dotnet format
  analyzers --diagnostics=RS0016` (the Roslyn code fix walks the
  assembly's public surface and writes canonical text in one pass —
  key strategy lesson applied after the hand-writing format
  headaches on KN-SELF-001). `IProcesses.RunAsync` dual-overload
  (`string[]` vs `string` first param) got a file-level
  RS0026 suppress with an explanation comment — overload resolution
  is unambiguous on parameter type, not optional-param count.
  CHANGELOG "### Changed" entry surfaces the flip to consumers.
  Remaining HasPublicApi=false modules: `*.EntityFrameworkCore`
  family + `Kun.AspNetCore` (future KN-SELF-xxx, evaluate
  per-module).
- **KN-SELF-008** — flip `Kun.Observability` to `HasPublicApi=true` —
  executor head `af4007c` (1 combined commit — AC-1 flag flip +
  AC-2 CHANGELOG folded) — 2026-04-24. Seeded empty `Shipped.txt`
  and full `Unshipped.txt` covering `KunObservabilityOptions`
  (12 properties + default ctor), `AddKunObservability` extension,
  and `KunBaggageKeys` (3 constants + `All` `static readonly`
  array). Format matches `Kun.Core` / `Kun.Mediator` precedent —
  `static readonly` for the field, inner-`!` on generic args.
  No behaviour change. Pilot for remaining `HasPublicApi=false`
  modules (future KN-SELF-xxx, one module at a time).
- **KN-SELF-007** — `Kun.Validation` Result-returning pipeline
  behavior (ADR-014, ADR-011 consumer) — executor head `2d7cc50`
  (ADR-014 + 4 code/test/doc commits) — 2026-04-24. Five ACs
  shipped, hit estimate exactly: ADR-014 accepted,
  `ValidationResultPipelineBehavior<TRequest, TValue>` with
  `where TRequest : IRequest<Result<TValue>>`, explicit
  `AddValidationResultBehavior<TRequest, TValue>()` DI extension on
  `KunMediatorBuilder` matching the existing `AddValidator<>` /
  `AddValidationBehavior<>` style, 5 new integration tests
  (empty-validators pass-through / validator-pass / validator-fail
  → `Result.Fail`/`request.invalid` / multi-validator aggregation /
  handler-level `Result.Fail` preserved untouched), AGENTS
  "Two pipeline behaviors ship — pick one" subsection +
  CHANGELOG entry. Kun.Validation ILC 0-warning; sln 0 errors;
  17/17 tests pass (12 existing + 5 new). Closes the
  "business-failures-use-Result-not-exceptions" rule enablement at
  the validation stage and makes ADR-011 `Result<T>` extensions a
  first-class pipeline consumer.
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
