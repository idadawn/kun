---
id: KN-SELF-001
title: Result<T> async extension methods + ADR-011
status: done
planner_commit: 951eba8
executor_head: 48937db
estimated_commits: 7
actual_commits: 7
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-001 — `Result<T>` async extension methods + ADR-011

## Why

`Kun.Core.Results.Result<T>` today exposes only `Ok` / `Fail` /
`EnsureSuccess`. Every consumer that composes async operations
(`neuroagent-csharp` Edge and Platform, future gateway handlers, Saga
steps) re-invents `Map` / `Bind` / `Tap` / `Ensure` ad-hoc — sometimes
without honoring short-circuit semantics, sometimes forgetting to
forward the `CancellationToken`. ADR-010 warned about exactly this
drift pattern at the cookie-session layer; fix it at the core before
more consumers copy the improvised version.

This task is **framework-driven** (BACKLOG P0, self-managed under the
two-role protocol). No business task is blocked on it today, but every
future `P1-KN-xxx` that composes async handlers will benefit — and
landing the ADR early means consumers pick up the canonical shape the
first time they reach for it.

## Specs to read first (non-negotiable)

Required by `kun/CLAUDE.md` §Specs-first. Reading order:

1. `kun/CLAUDE.md` — §Specs-first, §Architectural constraints
   (especially the `Result` / `Result<T>` vs exceptions rule)
2. `kun/AGENTS.md`
3. `kun/CONTRIBUTING.md` — commit + PR convention
4. `kun/src/Kun.Core/AGENTS.md` — zero-third-party-dep rule
5. `kun/src/Kun.Core/Results/AGENTS.md`
   (plus `Result.cs` / `Error.cs` current surface)
6. `kun/docs/architecture/ADR-010-Kun.Identity-cookie-session.md`
   — reference for ADR voice / structure
7. `kun/tests/Kun.Core.Tests/AGENTS.md`
8. `kun/Directory.Build.props` — `TreatWarningsAsErrors`,
   `GenerateDocumentationFile`, AOT analyzer switches
9. `kun/Directory.Packages.props` — confirm **no new packages needed**
   (the zero-dep rule in `Kun.Core` means all new code must be pure BCL)

## Acceptance criteria

- **AC-1**: `docs/architecture/ADR-011-result-async-extensions.md`
  accepted (Status: Accepted, dated 2026-04-24). Covers: method naming
  (`MapAsync` / `BindAsync` / `TapAsync` / `EnsureAsync`), decision to
  support both `Task<Result<T>>` and `ValueTask<Result<T>>` sources,
  cancellation-token propagation, short-circuit semantics, AOT
  constraints (no generic reflection, no `DynamicallyAccessed…`
  attributes on public API), and explicit rejection of alternative
  names (`ThenAsync`, `SelectAsync`, `Flatten`).
- **AC-2**: Synchronous extension surface landed in
  `src/Kun.Core/Results/ResultExtensions.cs`:
  - `Result.Tap(Action)`, `Result.Ensure(Func<bool>, Error)`,
    `Result.Match<T>(Func<T> ok, Func<Error, T> fail)`
  - `Result<T>.Map<TOut>(Func<T, TOut>)`,
    `Result<T>.Bind<TOut>(Func<T, Result<TOut>>)`,
    `Result<T>.Tap(Action<T>)`,
    `Result<T>.Ensure(Func<T, bool>, Error)`,
    `Result<T>.Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> fail)`
  All failure-path inputs short-circuit without invoking the supplied
  delegate; all public methods are XML-documented.
- **AC-3**: Async extensions on `Task<Result<T>>` sources landed:
  `MapAsync` / `BindAsync` / `TapAsync` / `EnsureAsync` plus
  `MatchAsync`, each accepting `CancellationToken` and propagating it
  to the delegate where the delegate accepts one. Every overload has a
  sync-delegate and an async-delegate form.
- **AC-4**: Async extensions on `ValueTask<Result<T>>` sources landed,
  mirroring AC-3. Rationale for the duplicate surface is the
  ADR-11 §Alternatives-considered (`ValueTask` for hot paths in
  Mediator pipeline; `Task` for user-facing handler code).
- **AC-5**: `tests/Kun.Core.Tests/Results/ResultExtensionsTests.cs`
  covers:
  - success pass-through for every `Map` / `Bind` / `Tap` / `Ensure`
  - failure short-circuit for every method (delegate must **not** be
    invoked — assert with a flag)
  - `CancellationToken` forwarding (assert delegate sees the supplied
    token, and a pre-cancelled token surfaces `OperationCanceledException`
    rather than an `Error`)
  - `Task` and `ValueTask` source parity for at least `MapAsync` and
    `BindAsync`
  - `Ensure`/`EnsureAsync` carry the supplied `Error` on predicate
    failure, preserve original error on already-failure inputs
- **AC-6**: `PublicAPI.Unshipped.txt` updated to list every new public
  method; `CHANGELOG.md [Unreleased]` gains a `### Added` entry
  citing ADR-011.
- **AC-7**: Build gates green on `develop` after final commit:
  `dotnet format --verify-no-changes`, `dotnet build Kun.sln -c Release
  -p:TreatWarningsAsErrors=true`, `dotnet test -c Release --no-build`
  (all non-Docker tests), and `dotnet publish src/Kun.Core -c Release
  -p:PublishAot=true /warnaserror` (local ILC pass — CI does the full
  link).

## Commit plan

| # | Commit subject | AC | Notes |
|---|----------------|----|-------|
| 1 | `docs(core): ADR-011 Result<T> async extension methods` | AC-1 | ADR-first per Specs rule |
| 2 | `feat(core): synchronous Map/Bind/Tap/Ensure/Match on Result(<T>)` | AC-2 | XML-docs, no new packages |
| 3 | `feat(core): async Result extensions on Task<Result<T>> sources` | AC-3 | include CT overloads |
| 4 | `feat(core): async Result extensions on ValueTask<Result<T>> sources` | AC-4 | mirror AC-3 shape |
| 5 | `test(core): cover Result extension sync + async + CT propagation` | AC-5 | xUnit v2, pure in-memory |
| 6 | `docs(core): CHANGELOG + PublicAPI.Unshipped for Result extensions` | AC-6 | list every new public symbol |
| 7 | `docs(core): Results/AGENTS.md usage examples for new extensions` | AC-6 | small follow-up doc, hand-curated examples |

Optional 8th commit (only if AOT publish flags anything): `fix(core):
AOT-quiet Result extensions` — otherwise omit.

## AOT / build gates that must stay green

- [ ] `dotnet format --verify-no-changes`
- [ ] `dotnet build Kun.sln -c Release -p:TreatWarningsAsErrors=true`
- [ ] `dotnet test tests/Kun.Core.Tests -c Release --no-build`
- [ ] `dotnet publish src/Kun.Core -c Release -p:PublishAot=true /warnaserror`
- [ ] `dotnet publish src/Kun.Mediator -c Release -p:PublishAot=true /warnaserror`
      (regression check — Mediator references Core)
- [ ] `PublicAPI.Unshipped.txt` diff non-empty
- [ ] `CHANGELOG.md [Unreleased]` diff non-empty
- [ ] Zero new `<PackageReference>` in `Kun.Core.csproj`
      (zero-third-party-dep rule in `src/Kun.Core/AGENTS.md`)

## Progress log

- 2026-04-24 3a5aa7d bootstrap — BACKLOG + autonomy dir landed (planner pre-step)
- 2026-04-24 951eba8 planner — pick KN-SELF-001, write active-task.md
- 2026-04-24 fad70c7 AC-1 — ADR-011 accepted
- 2026-04-24 8b113df AC-2 — sync Map/Bind/Tap/Ensure/Match landed on Result(<T>); PublicAPI.Unshipped updated in-commit
- 2026-04-24 02085a1 AC-3 — Task<Result<T>> async extensions landed; RS0026 drove CT-required decision (amendment in AC-6)
- 2026-04-24 450f278 AC-4 — ValueTask<Result<T>> async extensions landed; mirrors AC-3 on ValueTask rail
- 2026-04-24 fd80416 AC-5 — 41 new tests (85 total green); Task/ValueTask parity + CT forwarding + null guards
- 2026-04-24 0ce0cf7 AC-6 — CHANGELOG [Unreleased] section + ADR-011 §CancellationToken amendment recording required-CT rationale
- 2026-04-24 48937db AC-7 — Results/AGENTS.md usage examples (multi-step handler, Tap side-effect, Match fold)

## When done

1. Set `status: done` and `executor_head` to final commit SHA.
2. Move the KN-SELF-001 entry in `docs/roadmap/BACKLOG.md` from
   `P0` to `## Done` with SHA + date.
3. `echo "KN-SELF-001 done (<final SHA>) — Result<T> async extensions
   landed; ADR-011 accepted." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-001_done.txt`
4. Return to 角色1 and pick KN-SELF-002 or the next BACKLOG top.
