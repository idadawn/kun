---
id: KN-SELF-011
title: ADR-015 Outbox pattern design (retroactive documentation)
status: in-flight
planner_commit: <fill after commit>
executor_head: <fill after first executor commit>
estimated_commits: 2
started: 2026-04-24
---

# KN-SELF-011 — ADR-015 Outbox pattern design

## Why

The Outbox pattern is implemented and shipped across three modules
(`Kun.EventBus`, `Kun.EntityFrameworkCore`, consumer services), but
there is no single ADR walking a reader through the design:

- `kun/CLAUDE.md` §Outbox has the normative rules (atomicity,
  stable event IDs, idempotent consumers, W3C trace propagation)
  but those are distilled constraints, not design rationale.
- `docs/architecture/why-outbox.md` exists as a short primer but
  doesn't enumerate the type surface or the threading/locking
  model.
- `docs/architecture/ADR-004-Kun.Observability-module.md` through
  `ADR-014` each document one crisp decision; the Outbox rollout
  predates the ADR index and was never retroactively folded in.

KN-SELF-011 ships ADR-015 that captures the design of the
**existing** surface: why `OutboxMessage` carries
`TraceParent`/`TraceState`, why `IOutboxLockProvider` has a
Redis + Null pair, how `OutboxPublisherBackgroundService` pumps,
and the boundary between `Kun.EventBus` (abstractions) and
`Kun.EntityFrameworkCore` (where `OutboxAccumulator` actually
serialises into the write-side transaction).

## Specs to read first

1. `kun/CLAUDE.md` — §Outbox (the five normative rules).
2. `kun/docs/architecture/why-outbox.md` — primer.
3. `kun/src/Kun.EventBus/Outbox/OutboxMessage.cs` +
   `IIntegrationEventOutbox.cs` + `IOutboxWriter.cs` +
   `OutboxStatus.cs` — public outbox surface.
4. `kun/src/Kun.EventBus/Background/OutboxPublisherBackgroundService.cs`
   + `IOutboxMessageStore.cs` + `IOutboxLockProvider.cs` + the
   Null + Redis lock-provider impls.
5. `kun/src/Kun.EntityFrameworkCore/IntegrationEventOutboxAccumulator.cs`
   + `OutboxMessageConfiguration.cs` — where atomicity is
   enforced (`SaveChangesAsync` transaction).
6. `kun/src/Kun.EventBus/AGENTS.md`, `kun/src/Kun.EntityFrameworkCore/AGENTS.md`.

## Acceptance criteria

- **AC-1**: `docs/architecture/ADR-015-Outbox-pattern.md` written.
  Sections:
  - Context (why Outbox over direct publish).
  - Type surface (per namespace, brief purpose each).
  - Atomicity boundary (the `SaveChangesAsync` transaction).
  - Publisher pump (background service + lock provider + batching).
  - Consumer idempotency (`IProcessedMessageStore` + decorator —
    referenced, not re-specified).
  - W3C trace propagation (`TraceParent`/`TraceState` on the
    envelope; why baggage is NOT used for trace context here).
  - Stable event IDs (`{bounded-context}.{name}.v{n}`).
  - What Outbox does NOT do (read-side projections, saga
    orchestration — those have their own ADRs / modules).
  - Alternatives considered (direct publish, CDC, Kafka-source
    change logs) + why Outbox won.
- **AC-2**: Cross-link the new ADR from:
  - `kun/CLAUDE.md` §Outbox (one-line pointer — the constraint
    list stays; the ADR explains *why*).
  - `kun/src/Kun.EventBus/AGENTS.md` key-types table.
  - `kun/docs/architecture/why-outbox.md` (one-line pointer
    under a "See also" footer so the primer stays beginner-
    friendly).

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `docs(eventbus): ADR-015 Outbox pattern design` | AC-1 |
| 2 | `docs(eventbus,core): cross-link ADR-015 from CLAUDE.md / AGENTS / primer` | AC-2 |

## AOT / build gates

- [ ] No code changes — `dotnet build` unaffected (sanity run
      still 0/0).
- [ ] Markdown-only edits; `dotnet format --verify-no-changes` n/a.

## Out of scope

- Modifying the Outbox types themselves (any design-change ADR
  would be a separate task).
- Consumer-side migration guides; `docs/guides/` already covers
  the outbox-consumer happy path.
- New Outbox features like dead-letter handling, per-tenant
  priority queues, batch-size auto-tune — tracked elsewhere.

## Progress log

- 2026-04-24 d6985a7 KN-SELF-010 closed (prior cycle)

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-011 to BACKLOG `## Done`.
3. `echo "KN-SELF-011 done (<SHA>) — ADR-015 Outbox pattern documented." > /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-011_done.txt`
4. Planner next pick: open — candidates stay from KN-SELF-010
   close note (KN-SELF-007 F1, Kun.Testing.EntityFrameworkCore,
   or fresh pain point).
