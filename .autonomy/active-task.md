---
id: KN-SELF-003
title: Kun.Observability baggage-driven activity enricher
status: done
planner_commit: 014250b
executor_head: 12cf37c
estimated_commits: 7
actual_commits: 6
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-003 — `Kun.Observability` baggage-driven activity enricher

## Why

`KunActivitySources` already pre-registers the four framework sources
(`Kun.Mediator`, `Kun.EventBus.Outbox`, `Kun.Grpc`, `Kun.Saga`), but
nothing auto-tags `tenant_id` / `user_id` / `outbox.message_id` onto
the spans those sources emit. Operators reading Aspire / Jaeger end up
correlating spans to logs by hand whenever they need to filter by
tenant or user.

Add a baggage-driven enricher: consumer code pushes keys into
OpenTelemetry **baggage** (which is W3C-standard and auto-propagates
across HTTP / gRPC / async), and the framework mirrors a whitelisted
subset of baggage keys into span tags at `Activity.Start` time.
Baggage rides on the async-local context so we don't need to reach
into DI scopes from a singleton processor — that's the key design
simplification.

## Specs to read first (non-negotiable)

Required by `kun/CLAUDE.md` §Specs-first:

1. `kun/CLAUDE.md` — §Architectural constraints (Infrastructure
   layer + AOT strict)
2. `kun/AGENTS.md`
3. `kun/src/Kun.Observability/AGENTS.md` — "stop-gate AOT
   regressions on OTel upgrade"
4. `kun/src/Kun.Observability/KunActivitySources.cs` — canonical
   source list
5. `kun/src/Kun.Observability/Abstractions/KunObservabilityOptions.cs`
6. `kun/src/Kun.Observability/Extensions/ServiceCollectionExtensions.cs`
7. `kun/tests/Kun.Observability.Tests/AGENTS.md` (if present) or
   `AddKunObservabilityTests.cs` for conventions
8. `kun/docs/architecture/ADR-004-Kun.Observability-module.md` —
   prior module ADR for voice/structure
9. `kun/Directory.Packages.props` — confirm
   `OpenTelemetry.Exporter.InMemory` is available for tests (✅ seen
   during planning)

## Acceptance criteria

- **AC-1**: `docs/architecture/ADR-013-Kun.Observability-enricher.md`
  accepted. Records: baggage-to-tags design choice over
  DI-scope-spelunking, canonical baggage key names
  (`tenant.id`, `user.id`, `outbox.message_id`), why a
  `BaseProcessor<Activity>` + `OnStart` hook is the right place,
  AOT constraints, what is deliberately out of scope
  (Kun-log-enricher, metric-dimension enricher).
- **AC-2**: `src/Kun.Observability/Enrichment/KunBaggageKeys.cs` —
  public static class exposing the canonical baggage keys as string
  constants so consumers and tests reference the same names.
- **AC-3**: `src/Kun.Observability/Enrichment/KunBaggageActivityProcessor.cs`
  — `BaseProcessor<Activity>` that on `OnStart` checks the
  activity's `Source.Name` against `KunActivitySources.All`, reads
  the whitelisted baggage keys via `Activity.Baggage`, and copies
  each present key to a span tag (same name).
- **AC-4**: `KunObservabilityOptions.EnrichActivitiesFromBaggage`
  (default `false` — opt-in per ADR-013 §Default posture) + wire it
  into `AddKunObservability` so setting the flag registers the
  processor into the `TracerProviderBuilder`.
- **AC-5**: `tests/Kun.Observability.Tests` gain coverage via
  `OpenTelemetry.Exporter.InMemory`:
  - baggage `tenant.id` → span tag `tenant.id` on a Kun-sourced
    activity (positive path)
  - non-whitelisted baggage key ignored
  - activity from a non-Kun `ActivitySource` is not tagged
  - feature disabled → no tags added
- **AC-6**: Make `Kun.Observability.Enrichment` public surface —
  csproj flips `HasPublicApi` to `true` if not already, seeds
  `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` listing every
  new symbol. (Check current state in commit 1; may be a no-op.)
- **AC-7**: `src/Kun.Observability/AGENTS.md` + `CHANGELOG.md
  [Unreleased] ### Added` entry.

## Commit plan

| # | Commit subject | AC | Notes |
|---|----------------|----|-------|
| 1 | `docs(observability): ADR-013 span enricher from baggage` | AC-1 | ADR-first for behaviour-changing feature |
| 2 | `feat(observability): KunBaggageKeys canonical baggage-tag names` | AC-2 | Tiny — constants only; seeds the namespace |
| 3 | `feat(observability): KunBaggageActivityProcessor (OnStart enricher)` | AC-3 | BaseProcessor<Activity> + OnStart hook; no public DI wiring yet |
| 4 | `feat(observability): EnrichActivitiesFromBaggage option flag + DI wiring` | AC-4 | Opt-in flag, default false |
| 5 | `test(observability): InMemoryExporter covers baggage-to-tag copy` | AC-5 | 4+ tests per AC bullets |
| 6 | `docs(observability): PublicAPI seeding + AGENTS + CHANGELOG for enricher` | AC-6, AC-7 | May include flipping HasPublicApi=true |

Optional 7th only if AOT publish flags anything — otherwise omit.

## AOT / build gates that must stay green

- [ ] `dotnet format` on touched projects
- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0/0
- [ ] `dotnet test tests/Kun.Observability.Tests -c Release --no-build` green
- [ ] `dotnet publish src/Kun.Observability -c Release -r linux-x64 -p:PublishAot=true /warnaserror` ILC 0 warning
- [ ] `CHANGELOG.md [Unreleased]` has "### Added — Kun.Observability baggage-driven activity enricher"
- [ ] No new `<PackageReference>` in `Kun.Observability.csproj` (all OTel SDK types needed are already referenced)

## Out of scope

- Log-record enricher that tags `tenant.id` / `user.id` onto log
  entries — separate task because `OpenTelemetry.Logs.LogRecord` API
  differs from `Activity`. Tracked as ADR-013 §Follow-up F1.
- Metric dimensions — most Kun metrics are counters without labels
  today; dimensional enrichment is premature.
- Aspire sample screenshot / dashboard demo — `samples/Kun.Sample.*`
  are placeholders at the skeleton stage per repo status; revisit
  when samples are fleshed out. Tracked as ADR-013 §Follow-up F2.

## Progress log

<!-- append as commits land -->
- 2026-04-24 1c996f0 KN-SELF-002 closed (planner prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-003 in `BACKLOG.md` to `## Done`.
3. `echo "KN-SELF-003 done (<SHA>) — Kun.Observability baggage enricher landed." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-003_done.txt`
4. Planner next pick: KN-SELF-004 (Kun.Dapper AOT intent cleanup, BACKLOG P2) — small (S) warm-up before returning to larger P1 items.
