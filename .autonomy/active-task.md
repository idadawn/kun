---
id: KN-SELF-010
title: Complete HasPublicApi sweep — flip all remaining framework modules
status: done
planner_commit: 7af4efc
executor_head: 2eb3b17
changelog_commit: <this commit>
estimated_commits: 4
actual_commits: 4
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-010 — complete the HasPublicApi sweep

## Why

KN-SELF-008 and KN-SELF-009 piloted the `HasPublicApi=false` → `true`
flip on `Kun.Observability` and `Kun.Sandbox`. The mechanics are
identical across modules: flip the csproj flag, let
`dotnet format analyzers --diagnostics=RS0016` seed the
`PublicAPI.Unshipped.txt`, suppress any legacy RS0026 dual-overload
patterns at file level where they surface.

Eleven modules still carry `HasPublicApi=false`:

- `Kun.Audit`, `Kun.Audit.EntityFrameworkCore`
- `Kun.Caching`, `Kun.Configuration`
- `Kun.Identity`, `Kun.Identity.AspNetCore`
- `Kun.MultiTenancy`, `Kun.MultiTenancy.EntityFrameworkCore`
- `Kun.RateLimit`, `Kun.Security`, `Kun.Storage`

Per-module standalone tasks would burn ~11× the planner / close
overhead for what is essentially one command per module. Consolidate
into one sweep task, split into logical subsets per executor commit
so each diff remains reviewable.

## Specs to read first

1. `kun/CLAUDE.md` — §csproj / dependency rules.
2. `kun/.autonomy/active-task.md` history for KN-SELF-008 and
   KN-SELF-009 — the working shape.
3. Each module's existing `PublicAPI.*.txt` files (most are empty);
   inspect to see if any module already has seed entries that need
   updating rather than adding.
4. Per-module `AGENTS.md` — confirm no AOT / layering surprises
   that would make the flip inappropriate.

## Acceptance criteria

- **AC-1**: Flip the four "core-ish" modules — `Kun.Audit`,
  `Kun.Caching`, `Kun.Configuration`, `Kun.Storage`. Each gets its
  csproj flag flipped, `PublicAPI.Shipped.txt` seeded empty,
  `PublicAPI.Unshipped.txt` auto-populated via `dotnet format
  analyzers`. Fix any RS0026 at file level. One commit, one
  CHANGELOG bullet per module.
- **AC-2**: Flip the four "security / tenancy" modules —
  `Kun.Security`, `Kun.MultiTenancy`, `Kun.Identity`, `Kun.RateLimit`.
  Same mechanics.
- **AC-3**: Flip the three EF Core + AspNetCore satellites —
  `Kun.Audit.EntityFrameworkCore`,
  `Kun.MultiTenancy.EntityFrameworkCore`, `Kun.Identity.AspNetCore`.
  These are non-AOT modules; the flip is purely about API tracking,
  not AOT, and is valid regardless.
- **AC-4**: Single `CHANGELOG [Unreleased]` "### Changed" entry
  summarising the sweep (all 11 modules in one bullet list) —
  avoids 11 nearly-identical sections. Close the pilot: no
  `HasPublicApi=false` module remains (sanity-grep the sln).

## Commit plan

| # | Commit subject | AC | Notes |
|---|----------------|----|-------|
| 1 | `feat(audit,caching,configuration,storage): flip HasPublicApi=true + seed PublicAPI` | AC-1 | 4 modules in one commit; diff is mostly new `PublicAPI.Unshipped.txt` content |
| 2 | `feat(security,multitenancy,identity,ratelimit): flip HasPublicApi=true + seed PublicAPI` | AC-2 | 4 modules |
| 3 | `feat(identity.aspnetcore,audit.efcore,multitenancy.efcore): flip HasPublicApi=true + seed PublicAPI` | AC-3 | 3 modules |
| 4 | `docs(changelog): consolidated HasPublicApi sweep + sanity grep` | AC-4 | single CHANGELOG bullet, sanity-check remaining false |

Optional 5th commit only if a module surfaces unexpected RS0026 or other analyzer pushback that needs its own reasoning.

## AOT / build gates

- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true`
      0/0 after each executor commit (catch regressions per batch)
- [ ] `grep -rE "<HasPublicApi>false</HasPublicApi>" src/` returns
      **zero** results at end-of-task (the sanity grep in AC-4)
- [ ] No behaviour changes — tests unaffected across all modules

## Out of scope

- Updating `PublicAPI.Shipped.txt` with the Unshipped entries —
  those graduate on the next release cut, not during this sweep.
- Reviewing whether the *contents* of the auto-emitted entries
  are desirable (i.e. should any currently-public symbol be made
  internal). That's a separate API-review task; this one is pure
  tracking-enablement.
- Hand-writing XML doc comments where they are missing. Tracked
  as a future cleanup — the sweep surfaces them via CS1591 which
  is already allow-listed project-wide.

## Progress log

- 2026-04-24 34f65d9 KN-SELF-009 closed (prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-010 to BACKLOG `## Done` + mark the pilot chain
   (KN-SELF-008 → 009 → 010) as **complete** with a
   framework-wide summary.
3. `echo "KN-SELF-010 done (<SHA>) — HasPublicApi sweep complete; all framework modules now tracked." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-010_done.txt`
4. Planner next pick: open-ended — no pending sweep. Consider
   (a) KN-SELF-007 F1 (non-generic Result variant of validation
   behavior), (b) Kun.Testing.EntityFrameworkCore Outbox helpers
   (ADR-012 F1), or (c) a fresh pain point surfaced during the
   last nine tasks.
