---
id: KN-SELF-014-rescoped
title: Aspire Dashboard demo — baggage enrichment visible in Kun.Sample.AppHost
status: in-flight
planner_commit: <fill after re-pick commit>
executor_head: <fill after first executor commit>
estimated_commits: 3
started: 2026-04-24
rescope_note: |
  Original KN-SELF-014 pick was Kun.Testing.EntityFrameworkCore Outbox helpers.
  User (main controller) redirected to S-sized Aspire dashboard demo:
  expose ADR-013 baggage enrichment (tenant.id / user.id) in Aspire Dashboard
  by wiring Kun.Observability into Kun.Sample.Catalog and setting baggage
  at the request boundary.
---

# KN-SELF-014-rescoped — Aspire Dashboard baggage demo

## Why

ADR-013 shipped `KunBaggageActivityProcessor` + `KunBaggageLogRecordProcessor`,
but no sample service actually enables or demonstrates them. The Aspire
Dashboard (launched by `Kun.Sample.AppHost`) should show `tenant.id` and
`user.id` tags on traces so framework consumers see the enrichment contract
in action.

## Specs to read first

1. `kun/docs/architecture/ADR-013-Kun.Observability-enricher.md` §Decision + §F2.
2. `kun/src/Kun.Observability/AGENTS.md` — toggles and wiring.
3. `kun/src/Kun.Observability/Enrichment/KunBaggageKeys.cs` — canonical keys.
4. `kun/CLAUDE.md` — sample-app conventions.

## Acceptance criteria

- **AC-1**: `Kun.Sample.Catalog` replaces `Kun.Sample.Shared` OTel wiring with
  `Kun.Observability` reference. `AddKunObservability("catalog", o => {
  o.EnrichActivitiesFromBaggage = true; o.EnrichLogsFromBaggage = true; })`.
- **AC-2**: Catalog `/health` endpoint sets `tenant.id` + `user.id` baggage
  (mock values for demo) so the enricher mirrors them onto span tags.
- **AC-3**: `dotnet build Kun.sln -c Release /warnaserror` green.

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `feat(sample): wire Kun.Observability into Catalog with baggage enrichers enabled` | AC-1 |
| 2 | `feat(sample): set tenant.id + user.id baggage on Catalog health endpoint for demo` | AC-2 |

## AOT / build gates

- `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0 errors.
- `Kun.Observability` AOT publish remains 0-warning.

## When done

1. `status: done` + `executor_head` SHA.
2. `echo "KN-SELF-014-rescoped done (<SHA>) — Aspire dashboard baggage demo" > /home/dawn/project/neuroagent/.omc/state/kun_done.txt`
