<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Saga.Tests

## Purpose

Tests for `Kun.Saga` orchestrator: step sequencing, compensation order (reverse from failed step), compensation idempotency on partially-executed sagas, timeout detection, context serialization round-trip. Unit-level behavior only — end-to-end flows that involve EF Core and RabbitMQ live in `Kun.IntegrationTests`.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Saga.Tests.csproj` | xUnit v3 test project; references `Kun.Saga` |

## For AI Agents

### Working In This Directory

- **Must-cover test matrix**:
  - Happy path — all steps succeed, state → `Completed`.
  - Step N fails — compensations run for steps 0..N-1 in reverse, state → `Compensated`.
  - Compensation idempotency — compensation is safe to invoke when `ctx.ReservationId` etc. are null.
  - Timeout — `SagaTimeoutDetector` picks up stale `SagaInstance` rows and triggers compensation.
  - Context serialization — round-trip through `KunJsonContext` preserves all step-populated state.
- **Substitute the store, not the saga**: use `NSubstitute` on `ISagaInstanceStore` so you can assert "row was marked Compensating" without spinning up EF Core.
- **Cross-service step calls** (gRPC, external HTTP) are out of scope here — stub the `ISender` passed to steps.

### Testing Requirements

```bash
dotnet test tests/Kun.Saga.Tests
```

### Common Patterns

- Test saga `TestSaga : ISaga<TestContext>` with 3 predictable steps and configurable failure injection so one test can drive many scenarios.

## Dependencies

### Internal

- `Kun.Saga`, `Kun.Core`, `Kun.EventBus`

### External

- `xunit.v3`, `FluentAssertions`, `NSubstitute`

<!-- MANUAL: -->
