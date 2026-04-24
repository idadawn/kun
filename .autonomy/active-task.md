---
id: KN-SELF-018
title: docs/guides/orchestrating-sagas.md consumer guide
status: in-flight
planner_commit: <fill after commit>
executor_head: <fill after first executor commit>
estimated_commits: 2
started: 2026-04-24
---

# KN-SELF-018 — end-to-end Saga orchestration consumer guide

## Why

Three consumer-facing `docs/guides/` entries have been shipped
across KN-SELF-013 (validation), KN-SELF-017 (outbox). The
Saga subsystem has an architecture primer (`why-saga.md`) and
detailed agent-facing guardrails (`src/Kun.Saga/AGENTS.md`)
but no consumer-facing walkthrough — a downstream engineer
writing their first `ISaga<TContext>` + ordered steps +
compensation has to triangulate across:

- `docs/architecture/why-saga.md` (why orchestration over
  choreography)
- `src/Kun.Saga/AGENTS.md` (orchestration-only rule, atomic
  start, compensation idempotency, timeout, context
  serialization, stable step IDs)
- `src/Kun.Saga/Abstractions/ISaga.cs` +
  `ISagaStep.cs` + `ISagaOrchestrator.cs` + `ISagaContext.cs`
  (the interface contracts)
- `src/Kun.Saga/DependencyInjection/KunSagaServiceCollectionExtensions.cs`
  (registration shape)

Close the gap with one guide in the same style as
`validating-commands.md` / `outbox-and-integration-events.md`.

## Specs to read first

1. `kun/docs/architecture/why-saga.md` — prose framing to
   match.
2. `kun/src/Kun.Saga/AGENTS.md` — all the normative rules
   (orchestration only, atomic start, compensation
   idempotency, timeout + compensation order, context
   serialization via `KunJsonContext`, stable step IDs).
3. Real API shapes (already verified via grep — confirm
   before quoting):
   - `ISaga<TContext>`: `Name`, `DefaultStepTimeout`, `Steps`
   - `ISagaStep<TContext>`: `Name`, `Timeout`,
     `ExecuteAsync`, `CompensateAsync`
   - `ISagaOrchestrator.StartAsync<TSaga, TContext>(TContext,
     CancellationToken)` → `Task<Guid>`
   - `ISagaContext` base + consumer-defined context record
   - `KunSagaBuilder.AddSaga<TSaga, TContext>(JsonTypeInfo<TContext>)`
   - `AddKunSaga(services, Action<KunSagaBuilder>)`
   - `SagaState` enum values (Pending / Compensating /
     Compensated / Completed)
4. `kun/docs/guides/outbox-and-integration-events.md` +
   `kun/docs/guides/validating-commands.md` — style + length
   target (≤ 250 lines).

## Acceptance criteria

- **AC-1**: New `kun/docs/guides/orchestrating-sagas.md`.
  Sections:
  - **适用对象** — who this is for.
  - **Orchestration vs Choreography** — one-paragraph
    framing (why Kun chose Orchestration — central
    coordinator, compensation explicit, resumable across
    crashes).
  - **Saga + Step 接口** — concise reference for the four
    interface members engineers implement (`ISaga.Name`,
    `Steps`, `DefaultStepTimeout`; `ISagaStep.Name`,
    `Timeout`, `ExecuteAsync`, `CompensateAsync`).
  - **原子启动边界** — how `ISagaOrchestrator.StartAsync`
    persists the `SagaInstance` row + enqueues the first
    step Outbox message in the same `DbContext` transaction
    as the triggering command. ASCII/walk-through paralleling
    the KN-SELF-017 guide's SaveChangesAsync ladder.
  - **Context + JsonTypeInfo** — every `TContext` must be
    registered in `KunJsonContext` + passed at
    `AddSaga<TSaga, TContext>(JsonTypeInfo<TContext>)`
    registration. AOT-safe.
  - **Worked example** — 3-step `OrderCreationSaga`:
    reserve inventory → process payment → mark shipped;
    full `ISaga<OrderCreationContext>` + three
    `ISagaStep<OrderCreationContext>` classes with
    `ExecuteAsync` + `CompensateAsync` + stable `Name`
    strings + a `Timeout`. Registration via
    `services.AddKunSaga(b => b.AddSaga<OrderCreationSaga,
    OrderCreationContext>(OrderJsonContext.Default.OrderCreationContext));`.
    Command handler `StartAsync` call.
  - **Compensation + Timeout 生命周期** — what happens on
    step failure / timeout: reverse-order `CompensateAsync`
    runs through `SagaStepExecutor` (at-least-once, driven
    by Outbox), `SagaInstance.State` transitions
    `Pending → Compensating → Compensated` on rollback
    or `Pending → Completed` on success.
  - **幂等** — both `ExecuteAsync` AND `CompensateAsync`
    must be idempotent; "nothing to undo" is a normal
    compensation path (not an error).
  - **反模式** (4 items):
    (a) 跨 step 共享可变单例状态 — context is the only
        carrier, steps are transient by design;
    (b) 在 `CompensateAsync` 里抛异常兜不住 "nothing to
        undo" — block the compensation chain + leave saga
        stuck;
    (c) 用 CLR 类名当 step/saga ID — refactor-renames
        break persisted `SagaInstance` rows;
    (d) 往 Saga 里掺进 Choreography 元素 — step 发自己的
        integration event 让其他 step 订阅，散失中心可见性。
  - **边界 / 后续** — explicit links to
    `why-saga.md` + `P2-KN-Saga-EF-Split` (planned follow-up
    to restore `IsAotCandidate=true` by extracting
    `Kun.Saga.EntityFrameworkCore`) per the module's AGENTS
    note + `docs/architecture/AOT-audit-2026-04-23.md`.
  - **相关** footer: `why-saga.md`, `src/Kun.Saga/AGENTS.md`,
    sibling guides
    `outbox-and-integration-events.md` (Saga uses the Outbox
    for step execution) + `validating-commands.md`.
  - Target length ≤ 260 lines.

- **AC-2**: `src/Kun.Saga/AGENTS.md` picks up a one-line
  cross-link near Purpose pointing at the new guide as the
  consumer entry point. Surgical. Front-matter `Updated`
  bumped to 2026-04-24.

## Commit plan

| # | Commit subject | AC |
|---|----------------|----|
| 1 | `docs(saga): end-to-end orchestrating-sagas consumer guide` | AC-1 |
| 2 | `docs(saga): cross-link guide from AGENTS` | AC-2 |

## Gates

- [ ] Every relative link in the guide resolves (grep each
      target).
- [ ] No code-snippet references a type / method / property
      that does not exist in `src/Kun.Saga/` as of HEAD
      (already verified: `ISaga`, `ISagaStep`,
      `ISagaOrchestrator`, `KunSagaBuilder`, `SagaState`,
      `ISagaContext`).

## Out of scope

- Saga-specific test helpers (no
  `Kun.Testing.Saga` sibling yet — future work,
  note in edges footer).
- The `P2-KN-Saga-EF-Split` refactor itself (restoring
  AOT candidacy); only referenced.
- Distributed-transaction / 2PC comparison essay — the
  primer `why-saga.md` already covers that.
- Saga versioning / schema evolution deep dive.

## Progress log

- 2026-04-24 8ef076b KN-SELF-017 closed (Outbox consumer
  guide shipped). Planner hat on for KN-SELF-018.

## When done

1. `status: done` + `executor_head` SHA.
2. Move KN-SELF-018 to BACKLOG `## Done`.
3. `echo "KN-SELF-018 done (<SHA>) — end-to-end
   orchestrating-sagas consumer guide shipped; Saga story
   fully documented." >
   /home/dawn/project/neuroagent/.omc/state/claude-kun_KN-SELF-018_done.txt`
4. Next pick candidates: saas-multitenancy guide refresh
   (existing guide, may be stale vs recent ADRs), a
   `Kun.Testing.Saga` helper-ship if a concrete consumer
   need surfaces, ADR-013 §F2 with a small design ADR
   first, or fresh pain point.
