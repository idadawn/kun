---
id: KN-SELF-006
title: Kun.Observability baggage-driven log-record enricher (ADR-013 F1)
status: in-flight
planner_commit: <fill after commit>
executor_head: <fill after first executor commit>
estimated_commits: 5
started: 2026-04-24
---

# KN-SELF-006 — `Kun.Observability` log-record enricher

## Why

ADR-013 landed the span enricher that mirrors baggage
(`tenant.id`, `user.id`, `outbox.message_id`) onto Kun-sourced
activity tags. The companion work — mirroring the **same keys** onto
`LogRecord.Attributes` — was deferred as ADR-013 §F1. Without it,
operators filter spans by `tenant.id` in Aspire's trace UI but have
to fall back to hand-joining log records, because those same log
entries carry no `tenant.id` attribute.

Close the loop so a single Aspire / Jaeger / Kibana filter works
across both pillars.

## Specs to read first

1. `kun/CLAUDE.md` — §Architectural constraints + §AOT-per-module
2. `kun/docs/architecture/ADR-013-Kun.Observability-enricher.md` —
   span enricher rationale; §F1 sets the scope of this task and
   notes the OTel logs SDK surface differs from `Activity`.
3. `kun/src/Kun.Observability/Enrichment/KunBaggageActivityProcessor.cs`
   — mirror shape for the log-record version.
4. `kun/src/Kun.Observability/Enrichment/KunBaggageKeys.cs` —
   canonical key list (unchanged, shared with span enricher).
5. `kun/src/Kun.Observability/Extensions/ServiceCollectionExtensions.cs`
   — where `AddKunObservability` registers the logging pipeline.
6. `kun/tests/Kun.Observability.Tests/BaggageEnrichmentTests.cs` —
   test-shape precedent; reuse the ctor `Activity.Current = null`
   reset and the "force-build pipeline" pattern.
7. OTel 1.11.x `BaseProcessor<LogRecord>` surface — confirm
   `LogRecord.AttributeStorage` (or equivalent) is writable from a
   processor's `OnEnd`.

## Acceptance criteria

- **AC-1**: ADR-013 gains a "§F1 resolved" section recording the
  log-record-enricher design. Covers: why `OnEnd` again (same
  semantics as span; processor chain differs per pillar but the
  hook point is analogous), baggage-is-not-auto-on-LogRecord (the
  processor reads `Activity.Current?.Baggage`), preserving existing
  attributes (never overwrite a consumer-set `tenant.id` when the
  log site set it first).
- **AC-2**: `src/Kun.Observability/Enrichment/KunBaggageLogRecordProcessor.cs`
  — internal `BaseProcessor<LogRecord>` that on `OnEnd` reads
  `Activity.Current?.Baggage`, whitelists against
  `KunBaggageKeys.All`, and adds missing attributes to the
  `LogRecord`. Idempotent: if the log statement already set
  `tenant.id`, do **not** overwrite.
- **AC-3**: `KunObservabilityOptions.EnrichLogsFromBaggage` flag
  (default `false`, opt-in — matches span-enricher posture) + wire
  into `AddKunObservability` logging pipeline so setting the flag
  registers the processor into `OpenTelemetryLoggerOptions`.
- **AC-4**: `tests/Kun.Observability.Tests` gains a new
  `LogBaggageEnrichmentTests.cs` using
  `OpenTelemetry.Exporter.InMemory` for logs:
  - baggage `tenant.id` present on `Activity.Current` → exported
    `LogRecord.Attributes` contains `tenant.id`
  - flag disabled → attribute not added
  - log statement already set `tenant.id` → processor leaves the
    caller's value alone
  - no `Activity.Current` (background log without an active
    trace) → processor passes through (no crash, no attrs added)
- **AC-5**: `src/Kun.Observability/AGENTS.md` Key Types table gains
  the new processor row; `CHANGELOG.md [Unreleased]` adds a
  "### Added" subsection referencing ADR-013 §F1.

## Commit plan

| # | Commit subject | AC | Notes |
|---|----------------|----|-------|
| 1 | `docs(observability): ADR-013 §F1 resolved — log-record enricher` | AC-1 | ADR amendment in place; or new ADR-014 if amendment feels intrusive — decide in commit 1 |
| 2 | `feat(observability): KunBaggageLogRecordProcessor` | AC-2 | idempotent OnEnd; reads Activity.Current.Baggage |
| 3 | `feat(observability): EnrichLogsFromBaggage option + DI wiring` | AC-3 | registers against OpenTelemetryLoggerOptions |
| 4 | `test(observability): InMemoryExporter covers log-record baggage-to-attribute` | AC-4 | 4 tests per AC-4 bullets |
| 5 | `docs(observability): AGENTS + CHANGELOG for log-record enricher` | AC-5 | wrap-up |

## AOT / build gates

- [ ] `dotnet format` on touched projects
- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0/0
- [ ] `dotnet test tests/Kun.Observability.Tests -c Release --no-build`
      green (11 existing + 4 new = 15)
- [ ] `dotnet publish src/Kun.Observability -c Release -r linux-x64
      -p:PublishAot=true /warnaserror` ILC 0 warning (mirrors
      KN-SELF-003 verification)
- [ ] No new `<PackageReference>` on `Kun.Observability.csproj`
- [ ] `CHANGELOG.md [Unreleased]` gains the section referencing
      ADR-013 §F1

## Out of scope

- Mirroring baggage onto **metric dimensions** — most Kun metrics
  are counters without labels; dimensional enrichment premature
  (ADR-013 §Out of scope).
- Aspire sample dashboard demo (ADR-013 F2).
- Baggage propagation at request entry points — that's consumer
  responsibility; framework just mirrors what's on the ambient
  context.

## Progress log

- 2026-04-24 ff11848 KN-SELF-005 closed + KN-SELF-006 seeded into BACKLOG

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-006 in `BACKLOG.md` to `## Done`.
3. `echo "KN-SELF-006 done (<SHA>) — Kun.Observability log-record enricher landed." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-006_done.txt`
4. Planner next pick: fresh BACKLOG pass — the Saga EF split
   remains L-size parked; look for another M-sized item to seed
   (candidates: Mediator `ValidationBehavior` migration to ADR-011
   Result extensions; per-module PublicAPI flip sweep).
