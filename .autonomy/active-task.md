---
id: KN-SELF-009
title: Flip Kun.Sandbox to HasPublicApi=true (continue KN-SELF-008 sweep)
status: done
planner_commit: 7e5922a
executor_head: 5672525
estimated_commits: 2
actual_commits: 1
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-009 — flip `Kun.Sandbox` to `HasPublicApi=true`

## Why

Second step in the pilot started by KN-SELF-008 (`Kun.Observability`).
`Kun.Sandbox` is the next user-facing module still on
`HasPublicApi=false`. Public surface is ≈15 types + their members
(three `ISandboxProvider` implementations + the full set of E2B-
shaped DTOs and sub-APIs). Track them strictly so consumer-facing
shape changes pass the analyzer gate the way every other Kun.*
module already does.

## Specs to read first

1. `kun/CLAUDE.md` — §csproj / dependency rules.
2. `kun/docs/architecture/ADR-S02-Kun.Sandbox-module.md` — sandbox
   module decision; confirms the E2B-shaped API is intentional.
3. `kun/src/Kun.Sandbox/Kun.Sandbox.csproj` — HasPublicApi flag.
4. `kun/src/Kun.Core/PublicAPI.Shipped.txt` — reference format
   (including `static readonly` fields and `const` syntax).
5. `kun/src/Kun.Observability/PublicAPI.Unshipped.txt` —
   KN-SELF-008 precedent for the `#nullable enable` + sorted
   symbols layout.

## Acceptance criteria

- **AC-1**: `Kun.Sandbox.csproj` flips `HasPublicApi` to `true`;
  seed `PublicAPI.Shipped.txt` (empty `#nullable enable` header
  only) and `PublicAPI.Unshipped.txt` with every public symbol.
  Use `dotnet format analyzers --diagnostics=RS0016` to let the
  analyzer's code fix emit canonical symbol text — avoids
  hand-writing ~100 entries and ensures format matches exactly
  what the compiler expects.
- **AC-2**: Resolve RS0026 on `IProcesses.RunAsync` dual
  overloads (pre-existing surface — the two overloads differ in
  first-param type `string[]` vs `string`, not optional-param
  ambiguity). File-level `#pragma warning disable RS0026` with
  a comment explaining the overload-resolution shape. CHANGELOG
  "### Changed" entry so consumers notice.

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `feat(sandbox): flip HasPublicApi=true + seed PublicAPI (dotnet format auto-fill)` | AC-1 + AC-2 combined — single commit because the RS0026 suppression is the only diff outside the seeded files |

## AOT / build gates

- [ ] `dotnet build src/Kun.Sandbox -c Release -p:TreatWarningsAsErrors=true` 0/0
- [ ] No behaviour changes — tests unaffected.
- [ ] `CHANGELOG.md [Unreleased]` gets a "### Changed" entry.

## Progress log

- 2026-04-24 e780e1b KN-SELF-008 closed (prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-009 to BACKLOG `## Done`.
3. `echo "KN-SELF-009 done (<SHA>) — Kun.Sandbox HasPublicApi flipped on." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-009_done.txt`
4. Planner next pick: remaining `HasPublicApi=false` modules
   are the `*.EntityFrameworkCore` family plus `Kun.AspNetCore` —
   inspect whether the flip makes sense given they already carry
   consumer-facing extension surfaces.
