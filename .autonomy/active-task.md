---
id: KN-SELF-014
title: docs/guides/outbox-and-integration-events.md end-to-end guide + Kun.EventBus AGENTS cross-link refresh
status: in-flight
planner_commit: <fill after commit>
executor_head: <fill after first executor commit>
estimated_commits: 2
started: 2026-04-24
---

# KN-SELF-014 — Outbox + integration-events consumer guide

## Why

KN-SELF-011 shipped ADR-015 (Outbox pattern design), and the
framework already exposes the full surface — `IIntegrationEvent`,
`IIntegrationEventHandler<T>`, `IIntegrationEventBus`,
`IEventTypeRegistry`, `IOutboxPublisher`,
`IdempotentConsumerDecorator<T>`, stable event IDs,
`OutboxMessage` + EF transaction atomicity. A downstream
engineer wiring "publish `OrderPlaced` → consumer inserts
fulfillment row idempotently" today has to triangulate across:

- `docs/architecture/ADR-015-Outbox-pattern.md` (design,
  normative + rejected alternatives — not a quickstart)
- `docs/architecture/why-outbox.md` (primer — motivates the
  pattern but doesn't walk wiring)
- `src/Kun.EventBus/AGENTS.md` + `src/Kun.EventBus.RabbitMQ/AGENTS.md`
  (agent-facing guardrails)
- `src/Kun.EntityFrameworkCore/AGENTS.md` (the atomicity point
  lives here)
- `kun/CLAUDE.md` §Outbox (the rule statement)

Same gap shape KN-SELF-013 closed for validation: no
end-to-end recipe under `docs/guides/` for new consumers.

Close it with one guide + a surgical cross-link refresh of
`src/Kun.EventBus/AGENTS.md`. Doc-only — no code, no test,
no PublicAPI churn.

## Specs to read first (non-negotiable)

Required by `kun/CLAUDE.md` §Specs-first. Read before
writing:

1. `kun/CLAUDE.md` — §Outbox rule + §Architectural constraints
2. `kun/docs/architecture/ADR-015-Outbox-pattern.md` — full
3. `kun/docs/architecture/why-outbox.md` — prose framing to
   match
4. `kun/src/Kun.EventBus/AGENTS.md` — current guardrails
5. `kun/src/Kun.EventBus.RabbitMQ/AGENTS.md` — consumer wiring
6. `kun/src/Kun.EntityFrameworkCore/AGENTS.md` — atomicity site
7. `kun/src/Kun.EventBus/IIntegrationEvent*.cs` +
   `IEventTypeRegistry.cs` +
   `Idempotency/IdempotentConsumerDecorator.cs` +
   `Outbox/OutboxMessage.cs` (grep for real API shapes before
   quoting them in the guide)
8. `kun/docs/guides/gateway-rate-limit.md`,
   `kun/docs/guides/validating-commands.md` — style + length
   target

## Acceptance criteria

- **AC-1**: New `kun/docs/guides/outbox-and-integration-events.md`.
  Must include:
  - Opening "适用对象" — who this is for (framework consumers
    wiring their first integration event + downstream consumer).
  - **Atomicity boundary** section — the one-transaction rule
    (aggregate changes + outbox rows committed together in
    `SaveChangesAsync`). Sketch with a diagram (ASCII or a
    one-line `SaveChangesAsync` walkthrough).
  - **Stable event ID rule** — `{bounded-context}.{name}.v{n}`
    and the `IEventTypeRegistry` registration point. One-line
    reminder that `AssemblyQualifiedName` is forbidden.
  - **Publish example** — worked example: `OrderPlacedIntegrationEvent`
    definition, domain → integration mapping in the
    Command handler, EF Core `DbContext.SaveChangesAsync`
    commits both rows atomically, background `OutboxPublisher`
    pumps to RabbitMQ.
  - **Consume example** — worked example:
    `OrderPlacedIntegrationEventHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>`,
    explicit `IdempotentConsumerDecorator<T>` wrapping, what
    the at-least-once contract means for handler code (idempotency-
    key based on event ID + event-local fields if needed).
  - **W3C trace propagation note** — `TraceParent` / `TraceState`
    are captured on enqueue and replayed on publish; cross-service
    spans stitch together automatically. Distinction vs ADR-013
    baggage (baggage = user-scoped tenant/user IDs).
  - **Anti-patterns** section: (a) direct `IIntegrationEventBus.Publish`
    from within a domain operation (bypasses Outbox, breaks
    atomicity), (b) using `Type.AssemblyQualifiedName` as the
    event ID (breaks consumer resolution on assembly rename),
    (c) writing a consumer that's not idempotent (double-delivery
    on retry = data corruption), (d) mutating `MessageContext`
    across handlers (context is per-message, not a shared bag).
  - **Escape hatches / deferred** footer — point at ADR-015
    §F1 (dead-letter, not yet shipped), §F2 (per-tenant batch
    priority, not yet shipped), §F3 (Kun.Testing.EntityFrameworkCore
    Outbox helpers, not yet shipped) so consumers know where
    the edges are.
  - **相关** footer linking ADR-015, why-outbox.md, Kun.EventBus
    AGENTS, Kun.EventBus.RabbitMQ AGENTS, Kun.EntityFrameworkCore
    AGENTS, and docs/guides/validating-commands.md (sibling
    guide).
  - Target length: ≤ ~250 lines. Match prose density of
    gateway-rate-limit.md / validating-commands.md.

- **AC-2**: `src/Kun.EventBus/AGENTS.md` picks up a one-line
  cross-link near the top (below the Purpose section) pointing
  at the new guide as the consumer entry point. Front-matter
  `Updated:` bumped to 2026-04-24. No other edits — keep it
  surgical.

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `docs(eventbus): end-to-end outbox-and-integration-events guide` | AC-1 |
| 2 | `docs(eventbus): refresh AGENTS with guide cross-link` | AC-2 |

## AOT / build gates

Doc-only — code/AOT gates do not apply. Discipline checks:

- [ ] Every relative link in the new guide resolves
      (manual scan each `[…](…)` target).
- [ ] No code snippet references a type / method / property
      that does not exist in `src/Kun.EventBus/` or
      `src/Kun.EntityFrameworkCore/` as of HEAD (grep each
      name before committing).

## Out of scope

- Dead-letter implementation (ADR-015 §F1 — L-size, parked).
- Per-tenant batch priority (ADR-015 §F2).
- `Kun.Testing.EntityFrameworkCore` Outbox helpers (ADR-015
  §F3 — M-size new module, deferred).
- Diagrams beyond ASCII / one-line sketches (render tooling
  not in scope for this doc task).

## Progress log

- 2026-04-24 997c64b KN-SELF-013 closed + archived; planner
  hat on for KN-SELF-014 pick.

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-014 to BACKLOG `## Done`.
3. `echo "KN-SELF-014 done (<SHA>) — outbox + integration-events
   consumer guide shipped + Kun.EventBus AGENTS cross-linked." >
   /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-014_done.txt`
4. Planner next pick candidates: ADR-015 §F3
   Kun.Testing.EntityFrameworkCore Outbox helpers (M), ADR-013
   §F3 Aspire dashboard demo (S-M), Saga consumer guide (S,
   parallel to this one but for Saga), or fresh pain point.
