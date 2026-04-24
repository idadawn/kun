---
id: KN-SELF-017
title: docs/guides/outbox-and-integration-events.md consumer guide
status: in-flight
planner_commit: <fill after commit>
executor_head: <fill after first executor commit>
estimated_commits: 2
started: 2026-04-24
---

# KN-SELF-017 — end-to-end Outbox + integration-events consumer guide

## Why

KN-SELF-011 shipped ADR-015 (design), KN-SELF-014 shipped the
`Kun.Testing.EntityFrameworkCore` scaffold + `OutboxAssertions`,
and KN-SELF-016 closed the tests + docs loop (ADR-015 §F3
resolved). The only remaining consumer-facing gap in the
Outbox story is a `docs/guides/` walkthrough. Today a
downstream engineer writing their first integration-event
publisher + consumer has to triangulate across ADR-015
(design), why-outbox.md (primer), `Kun.EventBus/AGENTS.md`
(agent guardrails), `Kun.EventBus.RabbitMQ/AGENTS.md`
(broker wiring), `Kun.EntityFrameworkCore/AGENTS.md` (the
atomicity boundary), and the fresh `Kun.Testing.EntityFrameworkCore`
AGENTS (how to test). A single guide parallel to
KN-SELF-013's `docs/guides/validating-commands.md` closes
the gap.

(Note: this was KN-SELF-014's first pick at `c3ad04d` before
two rescopes; now landable as a well-sized S task because
the test-helper surface it would otherwise have to stub
is shipped.)

## Specs to read first

1. `kun/docs/architecture/ADR-015-Outbox-pattern.md` full
   (including §F3 resolved annotation from KN-SELF-016).
2. `kun/docs/architecture/why-outbox.md` — prose framing.
3. `kun/src/Kun.EventBus/AGENTS.md` + `kun/src/Kun.EventBus.RabbitMQ/AGENTS.md`.
4. `kun/src/Kun.EntityFrameworkCore/AGENTS.md`.
5. Real API shapes (grep before quoting):
   `IIntegrationEventBus.Publish<T>`,
   `IIntegrationEventHandler<TEvent>`,
   `KunEventBusBuilder.Register<T>(eventType[, typeInfo])`,
   `AddKunEventBus(builder)`, `AddKunOutboxPublisher`,
   `MessageContext` properties.
6. Style reference: `kun/docs/guides/validating-commands.md`
   (the sibling guide KN-SELF-013 shipped — match its
   Chinese-prose + English-code density).

## Acceptance criteria

- **AC-1**: New `kun/docs/guides/outbox-and-integration-events.md`.
  Sections:
  - 适用对象 — who this is for.
  - **原子性边界** (atomicity boundary) — one-transaction
    rule; one-line `SaveChangesAsync` walk-through.
  - **稳定事件 ID** — `{bounded-context}.{name}.v{n}` +
    `IEventTypeRegistry` registration; one-liner reminder
    that `AssemblyQualifiedName` is forbidden.
  - **发布示例** — worked example: define
    `OrderPlacedIntegrationEvent`, register via
    `AddKunEventBus(eb => eb.Register<OrderPlacedIntegrationEvent>("order.placed.v1"))`,
    `IIntegrationEventBus.Publish` inside a command handler
    / domain-event handler, the background `OutboxPublisher`
    pumps to RabbitMQ.
  - **消费示例** — worked example:
    `OrderPlacedIntegrationEventHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>.HandleAsync`,
    `IdempotentConsumerDecorator` wrapping (handled by DI —
    consumer writes a plain handler), at-least-once contract
    (handler must be safe to call repeatedly).
  - **测试** — brief section showing
    `dbContext.ShouldHaveExactlyOneOutboxMessage("order.placed.v1")`
    after the command handler saves, pointing at the
    KN-SELF-016-shipped assertions.
  - **W3C trace 传播** — `TraceParent` + `TraceState`
    captured on enqueue, replayed on publish; cross-service
    spans stitch. Distinction vs ADR-013 baggage (user-scope
    vs message-scope trace continuation).
  - **反模式** section (4 items):
    (a) direct `IIntegrationEventBus.Publish` from outside a
        transaction — event never persists atomically;
    (b) using `Type.AssemblyQualifiedName` as event ID;
    (c) writing a non-idempotent consumer handler —
        double-delivery on retry = data corruption;
    (d) mutating `MessageContext` across handlers — the
        context is per-message metadata, not a shared bag.
  - **边界 / 后续** footer linking ADR-015 §F1 (dead-letter,
    not shipped), §F2 (per-tenant priority, not shipped)
    so consumers know the edges.
  - **相关** footer: ADR-015, why-outbox.md,
    Kun.EventBus AGENTS, Kun.EventBus.RabbitMQ AGENTS,
    Kun.EntityFrameworkCore AGENTS,
    Kun.Testing.EntityFrameworkCore (test helpers),
    validating-commands.md (sibling guide).
  - Target length: ≤ ~250 lines.

- **AC-2**: `src/Kun.EventBus/AGENTS.md` picks up a one-line
  cross-link near the top (below Purpose) pointing at the new
  guide as the consumer entry point. Surgical — no other
  edits (Testing Requirements already picked up the
  OutboxAssertions cross-link in KN-SELF-016's closeout).

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `docs(eventbus): end-to-end outbox-and-integration-events guide` | AC-1 |
| 2 | `docs(eventbus): cross-link guide from AGENTS` | AC-2 |

## Gates

- [ ] Every relative link in the new guide resolves (grep
      each `(…)` target).
- [ ] No code-snippet references a type / method that does
      not exist in `src/Kun.EventBus/` or
      `src/Kun.EntityFrameworkCore/` as of HEAD — grep each
      name before commit.
- [ ] `dotnet format --verify-no-changes` (sanity; markdown
      isn't formatter-scope).

## Out of scope

- Dead-letter handling (ADR-015 §F1).
- Per-tenant batch priority (ADR-015 §F2).
- Saga-side guide — that's a larger sibling task for a later
  cycle.
- Chinese translation of ADR-015 itself — stays English;
  the guide is Chinese prose + English code, matching
  validating-commands.md.

## Progress log

- 2026-04-24 68dcd09 KN-SELF-016 closed (ADR-015 §F3
  resolved). Planner hat on for KN-SELF-017.

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-017 to BACKLOG `## Done`.
3. `echo "KN-SELF-017 done (<SHA>) — end-to-end
   outbox-and-integration-events consumer guide shipped." >
   /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-017_done.txt`
4. Next pick candidate: Saga consumer guide (sibling S task),
   or a deeper dive into one of the deferred §F2/§F1 items
   with a ralplan, or fresh pain point.
