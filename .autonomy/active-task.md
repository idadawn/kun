---
id: KN-SELF-013
title: docs/guides/validating-commands.md end-to-end guide + Kun.Validation AGENTS refresh
status: done
planner_commit: e9fe67a
executor_head: e3e9e45
estimated_commits: 2
started: 2026-04-24
finished: 2026-04-24
---

# KN-SELF-013 — end-to-end validation guide + AGENTS refresh

## Why

KN-SELF-007 shipped `ValidationResultPipelineBehavior<TRequest, TValue>`
and KN-SELF-012 shipped the non-generic `<TRequest>` sibling +
DI overload. Both ADR-011 and ADR-014 now describe a complete
validation story — but a downstream engineer opening
`kun/docs/guides/` today finds guides for rate-limit, read-side,
and multitenancy, and **no** guide for how to wire a command +
validator + Result pipeline end-to-end.

The gap matters because the choice between the two pipeline
behaviors (throwing vs Result, generic vs non-generic) is a
decision point at every handler, and today that decision is
only documented split across:

- `docs/architecture/ADR-014-Validation-Result-behavior.md`
  (deep design doc — not a quickstart)
- `src/Kun.Validation/AGENTS.md` "Two pipeline behaviors ship"
  subsection (agent-facing guardrail — also lists only the
  generic variant; the non-generic shipped after this was
  last edited)
- `CHANGELOG.md [Unreleased]` (release notes, not discovery)

A consumer writing a new `DeleteUserCommand : IRequest<Result>`
or `CreateOrderCommand : IRequest<Result<OrderDto>>` has no
single file to read to get started. Fix by:

1. Writing `docs/guides/validating-commands.md` — a
   consumer-facing end-to-end walkthrough with the decision
   tree, both variants, test pattern, and anti-patterns.
2. Refreshing `src/Kun.Validation/AGENTS.md` "Two pipeline
   behaviors ship" subsection to (a) mention the non-generic
   variant (it's stale post-KN-SELF-012), and (b) cross-link
   to the new guide.

Doc-only — no code / test / public-API surface changes.

## Specs to read first (non-negotiable)

Required by `kun/CLAUDE.md` §Specs-first. Read each before
writing:

1. `kun/CLAUDE.md` — §Specs-first + §Architectural constraints
   (especially "Business failures use `Result` / `Result<T>`,
   not exceptions")
2. `kun/docs/architecture/ADR-011-Result-async-extensions.md` —
   the `Result<T>` composition surface the Result-returning
   variant feeds into
3. `kun/docs/architecture/ADR-014-Validation-Result-behavior.md`
   (whole doc, especially §Scope and §F1-resolved note)
4. `kun/src/Kun.Validation/AGENTS.md` — current guardrail text
5. `kun/src/Kun.Validation/Behaviors/ValidationResultPipelineBehavior.cs`
   — both generic and non-generic classes, doc comments
6. `kun/src/Kun.Validation/Behaviors/ValidationPipelineBehavior.cs`
   — the throwing variant (for the decision tree)
7. `kun/src/Kun.Validation/DependencyInjection/KunValidationServiceCollectionExtensions.cs`
   — all DI overloads
8. `kun/tests/Kun.Validation.Tests/ValidationResultPipelineBehaviorTests.cs`
   — test patterns to show in the guide
9. `kun/docs/guides/gateway-rate-limit.md`,
   `kun/docs/guides/read-side-patterns.md`,
   `kun/docs/guides/saas-multitenancy.md` — existing guides,
   match their shape (headings, cross-link style, file length)

## Acceptance criteria

- **AC-1**: New file `kun/docs/guides/validating-commands.md`
  shipped. Must include:
  - Opening "who this is for" paragraph.
  - **Decision tree** section picking between the three
    behaviors (throwing / Result<TValue> / Result) with a
    table mapping handler return type → pick.
  - Worked example 1 — generic `Result<TValue>` command (e.g.
    `CreateOrderCommand : IRequest<Result<OrderDto>>` with
    two validators, DI wiring via
    `AddValidator<,>` + `AddValidationResultBehavior<,>()`,
    and a handler + test sketch showing the `Result.Fail`
    path).
  - Worked example 2 — non-generic `Result` command (e.g.
    `DeleteUserCommand : IRequest<Result>` with one
    validator, DI wiring via
    `AddValidator<,>` + `AddValidationResultBehavior<>()`,
    and a handler + test sketch).
  - **Anti-patterns** section: (a) do not register both
    behaviors for the same request type (double-wrap), (b)
    do not throw `ValidationException` inside a
    `Result`-returning handler, (c) do not put DB / network
    calls in a validator (semantic validation belongs in the
    handler).
  - **See also** footer linking ADR-011, ADR-014,
    Kun.Validation AGENTS.md, and
    `docs/architecture/when-to-use-currentuser.md`
    (as the Kun-idiomatic-doc-style exemplar).
  - Target length: ≤ ~200 lines. Match prose density of
    `gateway-rate-limit.md`.

- **AC-2**: `src/Kun.Validation/AGENTS.md` "Two pipeline
  behaviors ship" subsection updated:
  - Add a third bullet describing
    `ValidationResultPipelineBehavior<TRequest>` (non-generic)
    with its `IRequest<Result>` constraint and
    `AddValidationResultBehavior<TRequest>()` DI surface.
  - Change the closing "See [ADR-014]" line to also link to
    the new guide: "See [ADR-014](…) for design, or
    [docs/guides/validating-commands.md](../../docs/guides/validating-commands.md)
    for a consumer-facing walkthrough."
  - No other edits — keep it surgical.

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `docs(validation): end-to-end validating-commands guide` | AC-1 |
| 2 | `docs(validation): refresh AGENTS for non-generic variant + guide cross-link` | AC-2 |

(Estimate 2 commits — could be folded into 1 if the AGENTS
refresh is truly surgical, but keeping separate for review
clarity matches the "one-commit-per-sub-step" cadence rule.)

## AOT / build gates that must stay green

Doc-only task — the code/AOT gates do not apply. The
discipline checks are:

- [x] Doc-only change; `dotnet format` not applicable
      (markdown is not part of the formatter scope).
- [x] All relative links in the guide verified before
      commit: ADR-011, ADR-014, src/Kun.Validation/AGENTS.md,
      src/Kun.Testing/AGENTS.md,
      docs/architecture/when-to-use-currentuser.md.
- [x] All referenced type and extension-method names
      grepped against HEAD: `IRequest<>`, `Result`,
      `Result<>`, `Error.Validation`, `ErrorType.Validation`,
      `IValidator<>`, `ValidationResult.Failure`,
      `ValidationError`, `AddValidator<,>`,
      `AddValidationResultBehavior<TRequest>`,
      `AddValidationResultBehavior<TRequest, TValue>`,
      `AddKunMediator`, `AddRequestHandler`,
      `ResultAssertions.ShouldBeFailure/ShouldBeSuccess`.

## Out of scope

- Source-generator-based bulk registration (ADR-014 §F3).
- Structured per-field error payload (ADR-014 §F2).
- Adding the guide to a "table of contents" meta-page —
  `docs/guides/` has no index today; do not invent one.
- Translating any existing guides to Chinese (the repo
  mixes zh / en per-doc; match the tone of the three
  sibling guides, which are English).

## Progress log

- 2026-04-24 10c4a9a KN-SELF-012 closed + archived; planner
  hat on for KN-SELF-013 pick.
- 2026-04-24 e9fe67a planner pick + active-task brief (role 1).
- 2026-04-24 5f63663 executor commit 1 (AC-1): new
  docs/guides/validating-commands.md (~170 lines) — decision
  tree, CreateOrderCommand generic Result example,
  DeleteUserCommand non-generic Result example, anti-patterns,
  see-also footer. All cross-links + API names verified
  before push.
- 2026-04-24 e3e9e45 executor commit 2 (AC-2): surgical refresh
  of src/Kun.Validation/AGENTS.md — "Two pipeline behaviors
  ship" → "Three", new non-generic bullet, ADR-014 + guide
  cross-link, front-matter bump.
- 2026-04-24 hit 2-commit estimate exactly. Closing.

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-013 to BACKLOG `## Done`.
3. `echo "KN-SELF-013 done (<SHA>) — end-to-end validation
   guide shipped + Kun.Validation AGENTS refreshed." >
   /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-013_done.txt`
4. Planner next pick candidates: ADR-015 F3
   Kun.Testing.EntityFrameworkCore Outbox helpers (new
   module, M size), ADR-013 F2 metric dimensions from
   baggage (Kun.Observability, S-M), or fresh pain point
   surfaced while writing the guide.
