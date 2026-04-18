# Kun.Sample.Order

A self-contained sample that demonstrates `Kun.Saga` orchestration. A single `OrderCreationSaga`
drives an order through four compensatable steps, with demo endpoints that exercise the happy
path, payment-failure compensation, and timeout-triggered compensation.

---

## Saga flow

```
POST /demo/happy-path  (or trigger-payment-failure / trigger-timeout)
        │
        ▼
PlaceOrderCommandHandler
  ├─ Create Order (Pending)
  ├─ SaveChanges → order row committed
  └─ ISagaOrchestrator.StartAsync
       └─ saga_instances + saga_step_queue rows committed (same transaction)

Background: SagaStepExecutor polls saga_step_queue
        │
        ▼
Step 0 ─ ReserveInventoryStep.ExecuteAsync
         Logs "would gRPC Catalog", sets Order → Reserved
         CompensateAsync: cancel order (idempotent)
        │ success
        ▼
Step 1 ─ ProcessPaymentStep.ExecuteAsync
         Sets Order → Paid
         SimulateFailure="payment" → throws SagaStepFailedException
                                     → compensation starts from Step 0 backwards
         CompensateAsync: cancel order if Paid
        │ success
        ▼
Step 2 ─ ShipOrderStep.ExecuteAsync  [Timeout override: 10 s]
         Sets Order → Shipped
         SimulateFailure="timeout" → sleeps 60 s
                                     → SagaTimeoutDetector compensates after ~10 s
         CompensateAsync: cancel order if Shipped
        │ success
        ▼
Step 3 ─ NotifyCustomerStep.ExecuteAsync
         Sets Order → Completed
         Publishes OrderCompletedIntegrationEvent (order.completed.v1) via Outbox
         CompensateAsync: cancel order if Completed
        │ success
        ▼
SagaInstance.State = Completed
```

---

## What it demonstrates

| Concept | Where |
|---|---|
| Saga definition | `Application/Sagas/OrderCreationSaga.cs` |
| Saga context (shared state) | `Application/Sagas/OrderCreationSagaContext.cs` |
| Four compensatable steps | `Application/Sagas/Steps/` |
| Saga started inside a Command handler | `Application/Commands/PlaceOrderCommandHandler.cs` |
| `ISagaOrchestrator.StartAsync` | `PlaceOrderCommandHandler` — same UoW transaction as the `Order` row |
| Timeout configuration | `OrderCreationSaga.DefaultStepTimeout = 30 s`; `ShipOrderStep.Timeout = 10 s` |
| Outbox publish from a saga step | `NotifyCustomerStep` enqueues `order.completed.v1` |
| EF Core saga tables | `Infrastructure/OrderDbContext.cs` via `ApplyKunSagaConfiguration()` |
| Demo endpoints | `Api/Endpoints/DemoEndpoints.cs` |

---

## Demo endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/demo/happy-path` | All 4 steps succeed → `SagaState = Completed` |
| `POST` | `/demo/trigger-payment-failure` | `ProcessPaymentStep` throws → saga compensates Step 1, Step 0 → `Compensated` |
| `POST` | `/demo/trigger-timeout` | `ShipOrderStep` sleeps 60 s → `SagaTimeoutDetector` compensates → `Compensated` |
| `GET` | `/demo/sagas/{id}` | Returns `SagaInstance` state, current step index, failure reason |

### Example curl commands

```bash
# Set ORDER to the URL from the Aspire Dashboard (or http://localhost:5000 standalone)
ORDER=http://localhost:<port>

# Happy path
curl -s -X POST "$ORDER/demo/happy-path" | jq .

# Failure compensation
curl -s -X POST "$ORDER/demo/trigger-payment-failure" | jq .

# Timeout compensation (wait ~15 s before checking state)
curl -s -X POST "$ORDER/demo/trigger-timeout" | jq .

# Query saga state — use the orderId returned above
curl -s "$ORDER/demo/sagas/<orderId>" | jq .
```

---

## Expected saga state transitions

### Happy path

| Time | `State` | `CurrentStepIndex` |
|---|---|---|
| Immediately after POST | `Running` | 0 |
| After all steps complete | `Completed` | 3 |

### Payment-failure path

| Time | `State` | `CurrentStepIndex` |
|---|---|---|
| Immediately after POST | `Running` | 0 |
| After Step 1 throws | `Compensating` | 1 |
| After compensation finishes | `Compensated` | 0 |

### Timeout path

| Time | `State` | `CurrentStepIndex` |
|---|---|---|
| Immediately after POST | `Running` | 0 |
| During Step 2 (sleeping) | `Running` | 2 |
| After `SagaTimeoutDetector` fires | `Compensating` | 2 |
| After compensation finishes | `Compensated` | 0 |

---

## Prerequisites

- PostgreSQL (connection string in `appsettings.json` → `ConnectionStrings:OrderDb`, or override
  via environment variable).
- No RabbitMQ required: `NoOpIntegrationEventPublisher` logs to stdout instead.
- No JWT required: `NullTenantContext` is used (single-tenant sample).

---

## How to run

### Via AppHost (recommended)

```bash
dotnet run --project samples/Kun.Sample.AppHost
```

### Standalone

```bash
dotnet run --project samples/Kun.Sample.Order \
  --ConnectionStrings:OrderDb "Host=localhost;Database=order_sample;Username=postgres;Password=postgres"
```

The service calls `EnsureCreated` at startup. No migration step is needed for development.
