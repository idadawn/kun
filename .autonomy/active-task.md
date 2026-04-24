---
id: KN-SELF-008
title: Flip Kun.Observability to HasPublicApi=true
status: done
planner_commit: bdc8001
executor_head: af4007c
estimated_commits: 2
actual_commits: 1
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-008 — flip `Kun.Observability` to `HasPublicApi=true`

## Why

`Kun.Observability` has `<HasPublicApi>false</HasPublicApi>` today.
`KN-SELF-003` and `KN-SELF-006` added public surface here
(`KunBaggageKeys`, `KunObservabilityOptions.EnrichActivitiesFromBaggage`,
`EnrichLogsFromBaggage`, the `AddKunObservability` extension)
without the strict-tracking discipline that every other Kun module
with a user-facing API uses. The ADR-013 CHANGELOG entry explicitly
self-documented the follow-up.

This task flips the flag and seeds the two `PublicAPI.*.txt` files,
which from now on will force any public-symbol change to pass the
same analyzer check that guards the rest of the framework.

Pilot for a wider sweep of remaining `HasPublicApi=false` modules
(`Kun.Sandbox` and any sibling `*.EntityFrameworkCore` modules);
out of this task's scope — a future KN-SELF-xxx.

## Specs to read first

1. `kun/CLAUDE.md` — §csproj / dependency rules (Public API XML doc
   rule on `HasPublicApi=true` modules)
2. `kun/src/Kun.Observability/Kun.Observability.csproj` — flag lives here
3. `kun/src/Kun.Observability/AGENTS.md` — AOT / stop-gate rules
4. `kun/src/Kun.Core/PublicAPI.Shipped.txt` + `.Unshipped.txt` —
   reference for the format (inner `!` nullable annotation on
   reference-type generic arguments, etc. — lesson from KN-SELF-001)
5. `kun/src/Kun.Mediator/PublicAPI.Shipped.txt` — reference for
   extension-method entries pattern
6. All `src/Kun.Observability/**/*.cs` — enumerate public symbols

## Acceptance criteria

- **AC-1**: `src/Kun.Observability/Kun.Observability.csproj` flips
  `HasPublicApi` from `false` to `true`. Seed empty
  `src/Kun.Observability/PublicAPI.Shipped.txt` (just the
  `#nullable enable` header — convention: every new tracking file
  starts empty and accumulates as the next release cuts). Seed
  `src/Kun.Observability/PublicAPI.Unshipped.txt` listing every
  current public symbol: `KunObservabilityOptions` + every
  property getter/setter, `KunObservabilityServiceCollectionExtensions`
  + `AddKunObservability`, `Enrichment.KunBaggageKeys` + every
  const + `All` array.
- **AC-2**: `dotnet build src/Kun.Observability` stays 0/0 under
  `/warnaserror`. `CHANGELOG.md [Unreleased]` gets a short
  "### Changed" entry noting the flip so the next release notes
  surface it to consumers.

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `feat(observability): flip HasPublicApi=true + seed PublicAPI files` | AC-1 |
| 2 | `docs(observability): CHANGELOG entry for HasPublicApi flip` | AC-2 |

## AOT / build gates

- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true` 0/0
- [ ] `dotnet test tests/Kun.Observability.Tests -c Release --no-build`
      15/15 (no change expected)
- [ ] No code behavior changes — purely tracking-flag + file seeds
      + CHANGELOG

## Progress log

- 2026-04-24 3f35b1c KN-SELF-007 closed (prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-008 in `BACKLOG.md` to `## Done`.
3. `echo "KN-SELF-008 done (<SHA>) — Kun.Observability HasPublicApi flipped on." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-008_done.txt`
4. Planner next pick: either extend the sweep to `Kun.Sandbox`
   (next HasPublicApi=false module with user-facing surface) or
   seed a new M-size item from a fresh pain point.
