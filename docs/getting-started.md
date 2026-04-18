# Getting Started with Kun Framework

This tutorial takes about 10 minutes. By the end you will have the full sample stack running
locally, have triggered all three saga paths (happy, failure compensation, timeout compensation),
and have a mental model of how the pieces fit together.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.100 or later | Pinned in `global.json` |
| Docker Desktop | Any recent stable | Runs PostgreSQL, RabbitMQ, Redis containers |
| Aspire workload | Installed via `dotnet workload` | See step below |
| Visual Studio Build Tools (Windows only) | Optional | Required only for AOT native publish |

Verify your SDK version:

```bash
dotnet --version
# must print 10.0.100 or higher
```

Verify Docker is running:

```bash
docker info
# must succeed — no "Cannot connect" errors
```

---

## Install the Aspire workload

The AppHost project uses .NET Aspire to orchestrate all containers and services. Install the
workload once per machine:

```bash
dotnet workload install aspire
```

If the workload was already installed and you want to update it:

```bash
dotnet workload update
```

---

## Clone and build

```bash
git clone https://github.com/idadawn/kun.git
cd kun
dotnet restore
dotnet build -c Release
```

A successful build produces zero errors. Warnings about AOT trimming in `Kun.EntityFrameworkCore`
are expected and acceptable — that module is deliberately marked trim-safe rather than AOT.

Run the test suite to confirm everything is green:

```bash
dotnet test -c Release --no-build
```

---

## Run the samples

The AppHost project starts every service and all required infrastructure with a single command:

```bash
dotnet run --project samples/Kun.Sample.AppHost
```

Aspire prints a dashboard URL to the console, typically:

```
Login to the dashboard at: http://localhost:15888
```

Open that URL in a browser. You will see the **Aspire Dashboard** with:

| Panel | What it shows |
|---|---|
| Resources | Live health of catalog, order, gateway, postgres, rabbitmq, redis |
| Logs | Structured log streams per service, filterable by level |
| Traces | Distributed OpenTelemetry traces across all service boundaries |
| Metrics | Request rates, latency histograms, CPU and memory |

Wait until all resources in the Resources panel show a green status indicator before sending
requests. PostgreSQL runs schema creation (`EnsureCreated`) at startup, which takes a few seconds.

### Find the service ports

The Order service port is dynamic — Aspire assigns it at startup. In the Dashboard:

1. Click **Resources**.
2. Find the `order` row.
3. Copy the URL from the **Endpoints** column, for example `http://localhost:54321`.

Use that base URL in the curl commands below. The examples use `$ORDER` as a placeholder:

```bash
# Set this to the URL shown in the Aspire Dashboard for the "order" resource
ORDER=http://localhost:54321
```

---

## Walk through a happy-path order

Send a `POST` request to create an order that succeeds all four saga steps:

```bash
curl -s -X POST "$ORDER/demo/happy-path" | jq .
```

Example response:

```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "path": "happy-path"
}
```

Copy the `orderId` value. Query the saga state:

```bash
curl -s "$ORDER/demo/sagas/3fa85f64-5717-4562-b3fc-2c963f66afa6" | jq .
```

The response includes the current `SagaInstance` state. Immediately after the `POST` the saga is
`Running` as the background `SagaStepExecutor` works through the steps. Poll a few seconds later
and the state transitions to `Completed`:

```json
{
  "sagaId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "state": "Completed",
  "currentStepIndex": 3,
  "failureReason": null
}
```

**What happened:**

1. `PlaceOrderCommand` handler created an `Order` row and called `ISagaOrchestrator.StartAsync`.
2. The orchestrator inserted a `saga_instances` row (state `Running`) and the first
   `saga_step_queue` entry — all inside the same EF Core transaction as the `Order` row.
3. The background `SagaStepExecutor` picked up the queue entry and ran each step in order:
   - Step 0 `ReserveInventoryStep` — logs "would gRPC Catalog", sets order status `Reserved`
   - Step 1 `ProcessPaymentStep` — sets order status `Paid`
   - Step 2 `ShipOrderStep` — sets order status `Shipped`
   - Step 3 `NotifyCustomerStep` — sets order status `Completed`, enqueues `order.completed.v1`
     via the Outbox
4. `SagaInstance.State` was set to `Completed`.

Open **Traces** in the Aspire Dashboard and filter by `order`. You will see a single root trace
with child spans for each saga step, the EF Core queries, and the Outbox publish.

---

## Trigger the compensation path

This demonstrates what happens when a saga step fails: the orchestrator runs compensation steps
in reverse order.

```bash
curl -s -X POST "$ORDER/demo/trigger-payment-failure" | jq .
```

Response:

```json
{
  "orderId": "...",
  "path": "payment-failure",
  "note": "Saga will compensate ReserveInventory."
}
```

Query the saga state a moment later:

```bash
curl -s "$ORDER/demo/sagas/<orderId>" | jq .
```

Expected final state:

```json
{
  "sagaId": "...",
  "state": "Compensated",
  "currentStepIndex": 1,
  "failureReason": "ProcessPaymentStep failed: simulated payment failure"
}
```

**What happened:**

- `ProcessPaymentStep` detected `SimulateFailure = "payment"` in the saga context and threw
  `SagaStepFailedException`.
- The executor set `SagaInstance.State` to `Compensating` and enqueued `CompensateAsync` calls
  for steps 1 and 0 in reverse order.
- `ProcessPaymentStep.CompensateAsync` — cancels the order if status is `Paid`
- `ReserveInventoryStep.CompensateAsync` — cancels the order (idempotent)
- Final state: `Compensated`.

In the Aspire Dashboard Traces view you will see `saga.step.execute` spans for steps 0 and 1,
followed by `saga.step.compensate` spans for steps 1 and 0 in reverse.

---

## Trigger the timeout path

This demonstrates the `SagaTimeoutDetector` background service, which scans for stuck steps
every 5 seconds and kicks off compensation automatically.

```bash
curl -s -X POST "$ORDER/demo/trigger-timeout" | jq .
```

Response:

```json
{
  "orderId": "...",
  "path": "timeout",
  "note": "ShipOrderStep sleeps 60s — SagaTimeoutDetector compensates."
}
```

`ShipOrderStep` is configured with a 10-second per-step timeout override. When the step sleeps
for 60 seconds the `SagaTimeoutDetector` wakes up, finds the instance past its `TimeoutAt`
deadline, and triggers compensation from the current step index backwards.

After roughly 10–15 seconds:

```bash
curl -s "$ORDER/demo/sagas/<orderId>" | jq .
```

Expected:

```json
{
  "sagaId": "...",
  "state": "Compensated",
  "currentStepIndex": 2,
  "failureReason": "Step 2 timed out at ..."
}
```

---

## What you just saw

All three paths exercised the same underlying building blocks:

**Outbox transactional publishing** — every saga start and every integration event enqueue
happened inside a single EF Core `SaveChangesAsync` call. If the database transaction rolled back,
no message was produced. The `OutboxPublisherBackgroundService` drains `Pending` rows to the
broker asynchronously, so publish latency is measured in seconds but consistency is guaranteed.

**Saga orchestration** — the `SagaOrchestrator` is a central coordinator. It persists all state
in `saga_instances` and `saga_step_queue` tables, making the current state inspectable with a
plain SQL query at any time. There is no implicit choreography happening via event reactions.

**W3C Trace Context propagation** — every `OutboxMessage` row stores the `traceparent` and
`tracestate` headers captured at enqueue time. The background publisher resumes the trace when it
publishes, so a single `TraceId` threads through HTTP ingress → CQRS handler → Outbox → broker
→ consumer → downstream saga steps. This is visible in the Aspire Dashboard Traces panel.

---

## Next steps

- **Simpler CQRS story** — `samples/Kun.Sample.Catalog/` demonstrates DDD aggregates, write/read
  split with EF Core and Dapper, and Outbox event publishing without any saga involvement. Its
  [README](../samples/Kun.Sample.Catalog/README.md) walks through the file structure.

- **Architecture essays** — understand the design trade-offs:
  - [Why CQRS?](architecture/why-cqrs.md)
  - [Why Outbox?](architecture/why-outbox.md)
  - [Why Saga Orchestration?](architecture/why-saga.md)

- **Module map** — [`docs/architecture/module-map.md`](architecture/module-map.md) shows the
  full dependency graph and which modules are AOT candidates.

- **CLAUDE.md** — the binding architectural rules that every code contribution must satisfy,
  including the four-layer dependency constraint and AOT restrictions.
