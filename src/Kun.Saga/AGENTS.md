<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Saga

## Purpose

Saga Orchestration engine — a central coordinator that drives a sequence of `ISagaStep<TContext>` and, on failure, runs the completed steps' `CompensateAsync` in reverse order. Unlike Choreography (which Kun explicitly avoids), Orchestration keeps the flow visible in one place, makes compensation explicit, and persists state so the coordinator can resume after a crash or detect a timeout. Includes `SagaInstance` persistence, `SagaStepExecutor` background service, and `SagaTimeoutDetector`.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Saga.csproj` | `IsAotCandidate=true` |

## For AI Agents

### Working In This Directory

- **Orchestration only** — do not introduce patterns where each step publishes its own "next step" events for other steps to subscribe to. That's Choreography and it's outside the skill's scope for this framework.
- **Starting a saga is atomic with the triggering command**: `ISagaOrchestrator.StartAsync` persists `SagaInstance` and enqueues the first step message into the Outbox within the same `DbContext` transaction as the business command. Never split these.
- **Compensation rules**:
  - Runs in reverse order of executed steps (`for i = failedStepIndex; i >= 0; i--`).
  - Each compensation must be **idempotent**: safe to call when the forward step never ran, ran partially, or ran fully.
  - Compensation must not throw for "nothing to undo" states — that's a normal path.
- **Timeout**: every step has an optional `Timeout`; `SagaTimeoutDetector` scans `SagaInstance.TimeoutAt < UtcNow` every few seconds and moves the saga to `Compensating`.
- **Context serialization** uses `KunJsonContext.Default.GetTypeInfo(typeof(TContext))!`. Every saga context type must be registered in `KunJsonContext`.
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
