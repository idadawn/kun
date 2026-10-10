<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Saga

## Purpose

Saga Orchestration engine — a central coordinator that drives a sequence of `ISagaStep<TContext>` and, on failure, runs the completed steps' `CompensateAsync` in reverse order. Unlike Choreography (which Kun explicitly avoids), Orchestration keeps the flow visible in one place, makes compensation explicit, and persists state so the coordinator can resume after a crash or detect a timeout. Includes `SagaInstance` persistence, `SagaStepExecutor` background service, and `SagaTimeoutDetector`.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Saga.csproj` | `IsAotCandidate=false` (transitive EF Core — see `docs/architecture/AOT-audit-2026-04-23.md`; follow-up `P2-KN-Saga-EF-Split` will restore AOT candidacy by extracting `Kun.Saga.EntityFrameworkCore`) |

## For AI Agents

### Working In This Directory

- **Orchestration only** — do not introduce patterns where each step publishes its own "next step" events for other steps to subscribe to. That's Choreography and it's outside the skill's scope for this framework.
- **Starting a saga is atomic with the triggering command**: `ISagaOrchestrator.StartAsync` persists `SagaInstance` and enqueues the first step message into the Outbox within the same `DbContext` transaction as the business command. Never split these.
- **Compensation rules**:
  - Runs in reverse order of executed steps (`for i = failedStepIndex; i >= 0; i--`).
  - Each compensation must be **idempotent**: safe to call when the forward step never ran, ran partially, or ran fully.
  - Compensation must not throw for "nothing to undo" states — that's a normal path.
- **Scope and names**: AddSaga registers scoped saga factories; the scoped registry reads stable `ISaga.Name` and step counts after tenant restoration. Never build a temporary DI container. Existing CLR-name rows remain readable via the legacy alias; new rows use `ISaga.Name`.
- **Execution lock**: the default executor requires PostgreSQL and holds a SKIP LOCKED instance row lock through the step, context write-back, queue changes and transaction commit. Timeout detection takes the same lock and rechecks current state. Remote side effects remain at-least-once across crashes; steps must use idempotency keys.
- **Retry**: MaxAttempts defaults to 1 for compatibility. Opt in to bounded retries only for idempotent steps; NextAttemptAt persists exponential backoff (one-minute cap). Add the nullable column through a consumer migration before deployment (Order sample includes one). Failed local writes roll back to a savepoint; partial context persists for retry/compensation.
- **Timeout**: execution passes a linked cancellation token bounded by the current step deadline; steps must honor it. The detector catches expired idle/retry entries. Host shutdown rolls the current transaction back, without marking business failure or enqueuing compensation.
- **Context serialization** uses the JsonTypeInfo passed to AddSaga. Mutable context is serialized after execution/compensation, including partial failure, and persisted with the state transition.
- **Step identifiers are stable strings** (`"reserve-inventory"`, `"process-payment"`) — don't use class names; saga state persists across deployments where class names may be refactored.

### Testing Requirements

`tests/Kun.Saga.Tests` covers orchestration unit tests (step sequencing, compensation order, timeout). End-to-end cases that involve EF Core + RabbitMQ go in `tests/Kun.IntegrationTests`. Cover at least: happy path, step-N-fails → reverse compensation, timeout → compensation.

### Common Patterns

- `ISaga<TContext>` declares `Name`, `Steps`, `DefaultStepTimeout`.
- `ISagaStep<TContext>` has `ExecuteAsync` and `CompensateAsync`; steps typically call `ISender.Send(command)` to delegate work to local or remote services.
- Cross-service step execution happens via gRPC or integration events — either way, the step is responsible for awaiting the result before returning.

## Dependencies

### Internal

- `Kun.EventBus` — Outbox integration for step messages.
- `Kun.Core` — tenant context, Result.
- `Kun.Infrastructure` — `IUnitOfWork` for transactional state updates.

### External

- `Microsoft.Extensions.Hosting.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

<!-- MANUAL: -->
