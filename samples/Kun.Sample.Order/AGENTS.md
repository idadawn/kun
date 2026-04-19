# Kun.Sample.Order — Agent Notes

## Purpose

Demonstrates **Saga orchestration** using `Kun.Saga`. A single `OrderCreationSaga` drives an
order through four compensatable steps. Three demo endpoints exercise the happy path,
payment-failure compensation, and timeout-triggered compensation.

## Saga Flow

```
POST /demo/happy-path
        │
        ▼
PlaceOrderCommandHandler
  ├─ Create Order (Pending)
  ├─ SaveChanges → order row committed
  └─ ISagaOrchestrator.StartAsync
       └─ saga_instances + saga_step_queue rows committed

Background: SagaStepExecutor polls saga_step_queue
        │
        ▼
Step 0 ─ ReserveInventoryStep.ExecuteAsync
         Logs "would gRPC Catalog", sets Order → Reserved
         Compensate: Cancel order (idempotent)
        │ success
        ▼
Step 1 ─ ProcessPaymentStep.ExecuteAsync
         Sets Order → Paid
         SimulateFailure="payment" → throws SagaStepFailedException
                                     → compensation starts from Step 0 backwards
         Compensate: Cancel order if Paid
        │ success
        ▼
Step 2 ─ ShipOrderStep.ExecuteAsync
         Sets Order → Shipped
         SimulateFailure="timeout" → sleeps 60s (beyond 10s Timeout)
                                     → SagaTimeoutDetector triggers compensation
         Compensate: Cancel order if Shipped
        │ success
        ▼
Step 3 ─ NotifyCustomerStep.ExecuteAsync
         Sets Order → Completed
         Publishes OrderCompletedIntegrationEvent (order.completed.v1) via Outbox
         Compensate: Cancel order if Completed
        │ success
        ▼
SagaInstance.State = Completed
```

## Demo Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/demo/happy-path` | Creates an order; all 4 steps succeed → `Completed` |
| `POST` | `/demo/trigger-payment-failure` | ProcessPaymentStep throws → saga compensates Step 0 → `Compensated` |
| `POST` | `/demo/trigger-timeout` | ShipOrderStep sleeps 60 s → `SagaTimeoutDetector` fires compensation → `Compensated` |
| `GET`  | `/demo/sagas/{id}` | Returns `SagaInstance` state, current step index, failure reason |

## Prerequisites

- PostgreSQL (connection string in `appsettings.json` → `ConnectionStrings:OrderDb`)
- No RabbitMQ required: `NoOpIntegrationEventPublisher` is registered (logs to stdout)
- No JWT: `NullTenantContext` (single-tenant sample)

## Running

```bash
# Requires a reachable Postgres; EnsureCreated runs at startup
dotnet run --project samples/Kun.Sample.Order
curl -X POST http://localhost:5000/demo/happy-path
curl http://localhost:5000/demo/sagas/<saga-id-from-response>
```

## Key Files

| Path | Role |
|------|------|
| `Application/Sagas/OrderCreationSaga.cs` | Saga definition (Name, DefaultStepTimeout, Steps) |
| `Application/Sagas/OrderCreationSagaContext.cs` | Shared state (OrderId, SimulateFailure, …) |
| `Application/Sagas/Steps/` | 4 compensatable steps |
| `Application/Commands/PlaceOrderCommand.cs` | Entry point — creates Order + starts Saga |
| `Infrastructure/OrderDbContext.cs` | EF Core context; applies Kun Saga tables via `ApplyKunSagaConfiguration()` |
| `Api/Endpoints/DemoEndpoints.cs` | Minimal-API demo routes |

## Framework API Gaps Observed

None. All public APIs (`ISaga<T>`, `ISagaStep<T>`, `ISagaOrchestrator.StartAsync`,
`KunSagaBuilder.AddSaga`, `ISagaInstanceStore.FindAsync`) were present and matched the
skill documentation exactly.
