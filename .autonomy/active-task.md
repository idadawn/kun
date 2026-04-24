---
id: KN-SELF-004
title: Kun.Dapper AOT intent cleanup (AGENTS note + audit follow-up)
status: in-flight
planner_commit: <fill after commit>
executor_head: <fill after first executor commit>
estimated_commits: 2
started: 2026-04-24
---

# KN-SELF-004 — `Kun.Dapper` AOT intent cleanup

## Why

The AOT audit dated 2026-04-23 flagged `Kun.Dapper` YELLOW: no
explicit `<IsAotCandidate>` declaration. The csproj has since been
patched (**`<IsAotCandidate>false</IsAotCandidate>`** with a comment
pointing at the audit), but:

- `src/Kun.Dapper/AGENTS.md` "For AI Agents" section does not record
  the AOT intent, so a future agent modifying the file would not know
  why the flag is deliberately `false`.
- `docs/architecture/AOT-audit-2026-04-23.md` still lists Kun.Dapper
  as YELLOW in its TL;DR + module matrix + detail section + §后续建议
  — the stale status creates false signal during audits or when a
  new agent greps for "YELLOW" before picking up follow-ups.

Tiny cleanup to close the audit finding cleanly.

## Specs to read first

1. `kun/CLAUDE.md` — §Four-layer dependency flow (confirms
   `Kun.Dapper` sits above `Kun.EntityFrameworkCore` in the read
   split and is deliberately out of the AOT-candidate set because
   Dapper.AOT does its AOT-compat via source generator in consumer
   query handlers, not in the wrapper module)
2. `kun/docs/architecture/AOT-audit-2026-04-23.md` — the stale §YELLOW
   text needs a resolution marker
3. `kun/src/Kun.Dapper/Kun.Dapper.csproj` — already has the explicit
   flag + comment (sanity check)
4. `kun/src/Kun.Dapper/AGENTS.md` — target for the new AOT-intent
   note

## Acceptance criteria

- **AC-1**: `src/Kun.Dapper/AGENTS.md` "For AI Agents" section gains
  a bullet recording the AOT intent — `<IsAotCandidate>false</IsAotCandidate>`
  is deliberate; AOT coverage for read-side query handlers is each
  consumer's responsibility via `[DapperAot]`-annotated query
  methods, not this wrapper module's.
- **AC-2**: `docs/architecture/AOT-audit-2026-04-23.md` amended
  with a post-audit resolution marker. Keep the original YELLOW
  text as-is (history preservation) but add a clear "Resolved
  2026-04-24 in KN-SELF-004" line at each relevant spot (TL;DR,
  module matrix row, §YELLOW detail, §后续建议).

## Commit plan

| # | Commit subject | AC | Notes |
|---|----------------|----|-------|
| 1 | `docs(dapper): record AOT-intent in AGENTS.md` | AC-1 | single-file edit |
| 2 | `docs(audit): mark Kun.Dapper YELLOW resolved` | AC-2 | multi-spot edit in one audit file |

## AOT / build gates

- [ ] `dotnet format` on touched projects (docs only — may skip)
- [ ] `dotnet build src/Kun.Dapper -c Release -p:TreatWarningsAsErrors=true` still 0/0
- [ ] no csproj / code changes — AOT matrix unaffected

## Progress log

- 2026-04-24 5401f89 KN-SELF-003 closed (prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-004 in `BACKLOG.md` to `## Done`.
3. `echo "KN-SELF-004 done (<SHA>) — Kun.Dapper AOT intent cleanup." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-004_done.txt`
4. Planner next pick: KN-SELF-005 (IAuthenticatedUser naming-consistency
   sweep, BACKLOG P2 — another small S item).
