---
id: KN-SELF-015
title: Resolve NU1902 advisory on OpenTelemetry.Api 1.12.0 (unblock sample builds)
status: done
planner_commit: 3429965
executor_head: <fill at closeout>
estimated_commits: 2
started: 2026-04-24
finished: 2026-04-24
ship_log: |
  AC-1 shipped in ce910a4 (main controller, direct commit):
    Directory.Packages.props OpenTelemetry group bumped;
    OpenTelemetry.Api explicitly pinned to 1.15.3 (patched),
    companion pins on OpenTelemetry / Extensions.Hosting /
    Exporter.* raised to 1.12.0 for group coherence. CPM now
    force-resolves the transitive graph above the NU1902 CVE
    line.
  AC-2 shipped in <this closeout commit>: CHANGELOG.md
    [Unreleased] "Security" entry documents the advisory,
    the fix commit hash, the patched version, and downstream
    consumer guidance (align OpenTelemetry.Api on 1.15.3).
  Gate: `dotnet build Kun.sln -c Release
    -p:TreatWarningsAsErrors=true` now 0 errors (8 CS1591
    warnings in Aspire-generated AppHost code, pre-existing,
    excluded from TreatWarningsAsErrors per repo convention).
---

# KN-SELF-015 — resolve NU1902 on `OpenTelemetry.Api 1.12.0`

## Why

NuGet advisory refresh on 2026-04-24 (afternoon) flagged
`OpenTelemetry.Api 1.12.0` for
[GHSA-g94r-2vxg-569j](https://github.com/advisories/GHSA-g94r-2vxg-569j)
(moderate severity). Under `TreatWarningsAsErrors=true` (Release
build default), NuGetAudit promotes NU1902 to an error — breaking
`dotnet build Kun.sln -c Release` against both
`Kun.Sample.Catalog` and `Kun.Sample.Tests`. KN-SELF-014-rescoped
confirmed the failure is not regressed by any recent change — the
advisory itself is new — but it blocks every AOT / integration
verification path until resolved.

`OpenTelemetry.Api` is not pinned directly in
`Directory.Packages.props`; it enters transitively through:

- `OpenTelemetry 1.11.2`
- `OpenTelemetry.Instrumentation.EntityFrameworkCore 1.12.0-beta.1`

The beta-EF-Core instrumentation is what drags in the vulnerable
1.12.0 version. Fix by adding an explicit `OpenTelemetry.Api`
CPM entry pinned to a patched version (preferred), or by adding
a scoped `<NoWarn>NU1902</NoWarn>` to the two affected samples
with a justification comment pointing at the advisory URL
(fallback).

## Specs to read first (non-negotiable)

1. `kun/CLAUDE.md` — §csproj / dependency rules (CPM is central
   package management; no per-csproj `<Version=>`).
2. `kun/Directory.Packages.props` — OpenTelemetry group,
   currently lines 86–95.
3. `kun/Directory.Build.props` — whether `<NoWarn>` is the
   right pattern at repo level or per-project.
4. `samples/Kun.Sample.Catalog/Kun.Sample.Catalog.csproj`
   + `samples/Kun.Sample.Tests/Kun.Sample.Tests.csproj` — the
   two concrete sites that fail.
5. GHSA-g94r-2vxg-569j advisory page (consult for the fixed
   version range before committing a pin).

## Acceptance criteria

- **AC-1**: Build gate — `dotnet build Kun.sln -c Release
  -p:TreatWarningsAsErrors=true` returns 0 errors. Either
  the patched `OpenTelemetry.Api` pin in CPM resolves the
  transitive graph, or a scoped NoWarn is in place.
- **AC-2**: If the pin route is chosen, note the patched
  version + advisory URL in a CPM comment adjacent to the
  entry. If NoWarn fallback is chosen, every NoWarn
  occurrence carries an inline `<!-- GHSA-g94r-2vxg-569j ... -->`
  justification linking the advisory. Either way, the
  `CHANGELOG.md [Unreleased]` gets a one-line "Fixed" /
  "Security" entry referencing the advisory + resolution.

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `fix(deps): pin OpenTelemetry.Api to patched version (NU1902 / GHSA-g94r-2vxg-569j)` OR `chore(samples): NoWarn NU1902 with justification (fallback)` | AC-1 |
| 2 | `docs(changelog): record NU1902 / GHSA-g94r-2vxg-569j resolution` | AC-2 |

## AOT / build gates

- [ ] `dotnet restore Kun.sln` succeeds without version
      conflict.
- [ ] `dotnet build Kun.sln -c Release
      -p:TreatWarningsAsErrors=true` 0 errors.
- [ ] `dotnet build samples/Kun.Sample.Catalog -c Release
      -p:TreatWarningsAsErrors=true` 0 errors (specifically
      confirms the fix applied at the leaf project).
- [ ] Other modules' AOT publish unchanged — this is a sample/
      test-project-scope fix, should not affect `Kun.Core`,
      `Kun.Mediator`, etc.

## Out of scope

- Upgrading the full OpenTelemetry stack (1.11.x → 1.13.x or
  similar). Stays a targeted minor pin; broad upgrades need a
  dedicated task.
- Back-port fix for downstream consumers running against
  `OpenTelemetry.Instrumentation.EntityFrameworkCore`
  1.12.0-beta.1 directly — not a Kun framework concern.

## Progress log

- 2026-04-24 (pending) close out KN-SELF-014-rescoped →
  planner hat for KN-SELF-015.

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-015 to BACKLOG `## Done` + remove the P1
   follow-up entry.
3. `echo "KN-SELF-015 done (<SHA>) — NU1902 / GHSA-g94r-2vxg-569j resolved." >
   /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-015_done.txt`
4. Planner next pick: complete `Kun.Testing.EntityFrameworkCore`
   tests + docs (P3 follow-up in BACKLOG), or fresh pain
   point if the CVE fix surfaces something adjacent.
