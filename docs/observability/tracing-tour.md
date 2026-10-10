# Tracing Tour: Reading Distributed Traces in the Aspire Dashboard

This document walks through the OpenTelemetry traces you will see when running the Kun sample
stack via `dotnet run --project samples/Kun.Sample.AppHost`. It assumes Docker and the Aspire
workload are installed and the stack is running. For setup instructions see
[docs/getting-started.md](../getting-started.md).

---

## W3C Trace Context and OTLP

Every Kun service propagates traces using the
[W3C Trace Context](https://www.w3.org/TR/trace-context/) standard. Each in-process span records
a `traceparent` header that carries the `TraceId` and `SpanId`. When a request crosses a process
boundary — HTTP, gRPC, or the Outbox — the receiving side extracts the parent context and creates
a child span under the same `TraceId`. The result is a single trace tree that spans every hop
from the initial HTTP request to the final consumer acknowledgement.

Spans are exported via OTLP. When running under .NET Aspire the `OTEL_EXPORTER_OTLP_ENDPOINT`
environment variable is set automatically to the Aspire Dashboard's collector endpoint. The
Dashboard collects and renders all spans without any additional configuration in the services
themselves. Framework modules (`src/Kun.*`) contain no OTel package dependencies — all
`AddKunObservability` wiring lives in the sample composition root
(`samples/Kun.Sample.Shared/OpenTelemetryExtensions.cs`) so the framework remains AOT-clean.

---

## What Instruments What

### Kun.Mediator

**ActivitySource name:** `Kun.Mediator`

`TracingBehavior<TRequest, TResponse>` wraps every `ISender.Send` and `IPublisher.Publish` call
(see `src/Kun.Mediator/Behaviors/TracingBehavior.cs`). It starts an `ActivityKind.Internal` span
named `Mediator.Send {RequestTypeName}` — for example `Mediator.Send CreateProductCommand` or
`Mediator.Send GetProductByIdQuery`.

The span is a child of the ambient activity at dispatch time, which is typically the ASP.NET Core
server span for the incoming HTTP request. Because the Mediator span is internal, it appears as a
nested block directly under the HTTP span in the Dashboard timeline.

Tags set on this span:

| Tag | Value |
|-----|-------|
| (none beyond span name) | The request type name is encoded in the span name |

If you want to add custom tags (e.g. command ID, tenant ID), do so from within the handler by
calling `Activity.Current?.SetTag(...)`.

### Kun.EventBus.Outbox

**ActivitySource name:** `Kun.EventBus.Outbox`

`OutboxPublisherBackgroundService` starts one `ActivityKind.Producer` span named `outbox.publish`
for each `OutboxMessage` it successfully publishes to the broker (see
`src/Kun.EventBus/Background/OutboxPublisherBackgroundService.cs`).

The critical detail is that the **parent context is restored from the stored row**, not from the
current ambient activity. When `EfUnitOfWork.SaveChangesAsync` writes an `OutboxMessage` row, the
`IntegrationEventOutboxAccumulator` captures `Activity.Current?.Id` and
`Activity.Current?.TraceStateString` and stores them in the `TraceParent` and `TraceState`
columns of the row. The background service reads those columns back hours or seconds later and
calls `ActivityContext.TryParse(message.TraceParent, message.TraceState, ...)` to reconstruct the
parent context before starting the publish span.

This means the `outbox.publish` span appears as a **child of the original producer span** in the
Dashboard, even though it runs in a different process tick, in a different background service
invocation, potentially seconds after the HTTP response was already sent to the client.

Tags set on `outbox.publish`:

| Tag | Value |
|-----|-------|
| `messaging.system` | `outbox` |
| `messaging.destination` | Stable event type ID, e.g. `product.published.v1` |
| `messaging.message_id` | UUID of the `OutboxMessage` row |

### Kun.Grpc

**ActivitySource name:** `Kun.Grpc`

`KunTracingInterceptor` (see `src/Kun.Grpc/Interceptors/KunTracingInterceptor.cs`) runs first in
the interceptor chain registered by `AddKunGrpc`. It starts an `ActivityKind.Server` span named
`grpc.server` for each unary, client-streaming, server-streaming or duplex RPC call. The span
remains active until the awaited handler completes, including all stream operations.

Tags set:

| Tag | Value |
|-----|-------|
| `rpc.system` | `grpc` |
| `rpc.method` | Full gRPC method path, e.g. `/catalog.CatalogService/GetProduct` |
| `rpc.type` | `unary`, `client_streaming`, `server_streaming` or `duplex_streaming` |
| `rpc.grpc.status_code` | Final gRPC status code; faults also mark the activity as Error |

If a downstream service calls a Kun gRPC service, the caller's `HttpClient` instruments propagate
the `traceparent` header via the standard gRPC client instrumentation wired in
`AddKunObservability`. The server interceptor picks up the parent context automatically from the
incoming metadata — `ActivityKind.Server` causes the OTel SDK to look for an inbound W3C header.

### Kun.Saga

**ActivitySource name:** `Kun.Saga`

`SagaStepExecutor` (see `src/Kun.Saga/Orchestration/SagaStepExecutor.cs`) starts one
`ActivityKind.Internal` span per step queue entry it processes. The span name encodes the
direction:

- `saga.step.execute` — forward execution of a step
- `saga.step.compensate` — reverse compensation of a step

Tags set:

| Tag | Value |
|-----|-------|
| `saga.step_index` | Zero-based index of the step in the saga definition |
| `saga.saga_id` | UUID of the `SagaInstance` |
| `saga.direction` | `Execute` or `Compensate` |
| `saga.name` | Registered saga class name, e.g. `OrderCreationSaga` |

Note: these spans are started by the `SagaStepExecutor` background service polling loop. The
background service does **not** restore a producer trace context from the saga instance row — the
saga spans therefore appear as root spans or under whatever ambient activity the poll loop happens
to inherit. Cross-trace correlation via `saga.saga_id` is the intended mechanism; see the
[saga_id as cross-trace correlation](#sagaid-as-cross-trace-correlation) section below.

---

## How to Read a Happy-Path Trace

The happy path is triggered by `POST /demo/happy-path` against the Order service. This is what
you should see in the Aspire Dashboard Traces panel:

```
[HTTP POST /demo/happy-path]  ← ASP.NET Core server span (root)
  └─ [Mediator.Send PlaceOrderCommand]  ← TracingBehavior span
       └─ [outbox.publish order.completed.v1]  ← OutboxPublisherBackgroundService span
            (parent context restored from OutboxMessage.TraceParent)
```

The saga step spans appear separately because the `SagaStepExecutor` runs in a background
service:

```
[saga.step.execute]  saga.step_index=0, saga.name=OrderCreationSaga
[saga.step.execute]  saga.step_index=1, saga.name=OrderCreationSaga
[saga.step.execute]  saga.step_index=2, saga.name=OrderCreationSaga
[saga.step.execute]  saga.step_index=3, saga.name=OrderCreationSaga
```

To correlate these saga step spans with the originating HTTP request, use the `saga.saga_id` tag
(see the next section). Filter by the saga UUID in the Dashboard search bar to find all spans
associated with a single saga instance.

The timeline for the happy path looks like this in wall-clock order:

1. `POST /demo/happy-path` arrives. ASP.NET Core opens a server span.
2. `PlaceOrderCommand` is dispatched via `ISender.Send`. `TracingBehavior` opens a child span.
3. The command handler calls `ISagaOrchestrator.StartAsync`. This writes a `SagaInstance` row and
   the first `SagaStepQueueEntry` row inside the same EF Core `SaveChangesAsync` transaction.
4. The HTTP response is sent. The server span closes.
5. Seconds later, `SagaStepExecutor` polls `FetchPendingStepsAsync` and finds the queue entry.
   It opens a `saga.step.execute` span and runs `ReserveInventoryStep.ExecuteAsync`. On success,
   it enqueues step 1 and marks the entry processed.
6. Steps 1, 2, and 3 execute in sequence in subsequent poll cycles.
7. `NotifyCustomerStep` enqueues an `order.completed.v1` `OutboxMessage` row inside its
   `SaveChangesAsync` call. The `TraceParent` of the current `saga.step.execute` span is captured
   into the row.
8. `OutboxPublisherBackgroundService` polls, finds the pending message, restores the trace
   context from `TraceParent`, and opens an `outbox.publish` child span. The message is published
   to RabbitMQ and the row is marked `Published`.

In the Dashboard, the `outbox.publish` span correctly appears as a descendant of the saga step
that produced it, giving you the full causal chain in one view.

---

## How to Read a Compensation Trace

The compensation path is triggered by `POST /demo/trigger-payment-failure`. The trace structure
reflects the point of failure and the reverse execution:

```
[HTTP POST /demo/trigger-payment-failure]
  └─ [Mediator.Send PlaceOrderCommand]
```

Then in background poll cycles:

```
[saga.step.execute]  step_index=0  (ReserveInventoryStep — succeeds)
[saga.step.execute]  step_index=1  (ProcessPaymentStep — fails, span status=Error)
[saga.step.compensate]  step_index=1  (ProcessPaymentStep.CompensateAsync)
[saga.step.compensate]  step_index=0  (ReserveInventoryStep.CompensateAsync)
```

When `ProcessPaymentStep.ExecuteAsync` throws `SagaStepFailedException`, the executor:

1. Sets `activity?.SetStatus(ActivityStatusCode.Error, ex.Message)` on the failing span — visible
   as a red span in the Dashboard.
2. Transitions the `SagaInstance` to `SagaState.Compensating`.
3. Enqueues `Compensate` entries for steps 1 and 0 in reverse order.

The compensation spans appear with `saga.direction=Compensate` in their tags. Because they run in
the same background service with the same `ActivitySource`, they appear in the Dashboard's "all
spans" list alongside the execution spans. Searching by `saga.saga_id` brings them together.

To distinguish failure from normal completion in the Dashboard:

- A `saga.step.execute` span with `ActivityStatus=Error` is the failing step.
- Following `saga.step.compensate` spans at lower `step_index` values are the rollback.
- The final `SagaInstance.State` stored in the database is `Compensated`.

---

## saga_id as Cross-Trace Correlation

Because saga steps run in a background poll loop that does not inherit the HTTP request's trace
context, each saga step span is technically a separate root trace in OTLP terms. The `saga.saga_id`
tag set on every step span is the mechanism for correlating them.

In the Aspire Dashboard:

1. Trigger any demo endpoint and note the `sagaId` in the response body.
2. Open **Traces** and use the attribute search or filter bar to search for
   `saga.saga_id = <your-uuid>`.
3. All `saga.step.execute` and `saga.step.compensate` spans for that saga instance appear,
   giving you a complete step-by-step history.

The tag name is `saga.saga_id` and its value is the `SagaInstance.Id` UUID. This tag is set in
`SagaStepExecutor.ProcessEntryAsync` via `activity?.SetTag("saga.saga_id", entry.SagaId.ToString())`.

Note for contributors: connecting saga step spans back to the originating HTTP trace via a stored
`TraceParent` on the `SagaInstance` row — similar to how `OutboxMessage` does it — would allow
the full saga lifecycle to appear as one unified trace tree. This is not currently implemented
and is tracked as future work.

---

## How to Read Outbox Lag

Outbox lag is the time between when an integration event was enqueued (the producer's
`SaveChangesAsync` commit) and when it was actually published to the broker (the
`outbox.publish` span).

To measure lag in the Dashboard:

1. Find the `Mediator.Send` span for a command that produces an integration event (e.g.
   `CreateProductCommand`).
2. Note its **end timestamp** — this is approximately when `SaveChangesAsync` committed and the
   outbox row was written.
3. Find the `outbox.publish` span for the same message (match by `messaging.message_id`, or by
   `messaging.destination` + approximate time).
4. The **start timestamp** of `outbox.publish` minus the end timestamp of the producer span is
   the outbox lag for that message.

Under normal conditions with the default `OutboxPublisherOptions.PollInterval` of 2 seconds and
`BatchSize` of 50, lag is typically 0–4 seconds. Under heavy load, lag grows as the batch fills.
Reduce `PollInterval` or increase `BatchSize` to tune.

The `OutboxPublisherOptions` defaults:

| Setting | Default | Effect |
|---------|---------|--------|
| `BatchSize` | 50 | Messages processed per poll cycle |
| `PollInterval` | 2 seconds | Sleep between cycles when queue is non-empty |
| `MaxAttempts` | 5 | Publish attempts before transitioning row to `Failed` |
| `RetryBaseDelay` | 1 second | Exponential backoff base delay |

A message in `OutboxStatus.Failed` will not be retried automatically. Monitor for `Failed` rows
in the `outbox_messages` table and alert on them — they represent messages that could not be
delivered after all attempts.

---

## Pitfalls and Common Gaps

### Background services that do not start an activity

`SagaStepExecutor` and `OutboxPublisherBackgroundService` are `BackgroundService` implementations.
Their `ExecuteAsync` loop does not have an ambient `Activity` unless one is explicitly started.
This means:

- Any log message emitted inside the loop but outside a span will have no `TraceId` attached in
  structured logs.
- Any child activity started without an explicit parent context appears as a root span in OTLP.

If you add custom background services to your application and want their spans connected to
a broader trace, you must restore the parent context manually — the same pattern used by
`OutboxPublisherBackgroundService` with `ActivityContext.TryParse`.

### async void timers and fire-and-forget

Avoid `async void` methods and `_ = Task.Run(...)` inside handlers or background services.
Exceptions thrown on `async void` paths crash the process without ending any open spans, leaving
dangling incomplete spans in the Dashboard. Use `async Task` and `await` throughout.

### Gateway observability

The standalone `Kun.Gateway` host registers `AddKunObservability` with service name
`kun-gateway`, configured through `KunObservability`. EF instrumentation is disabled because
this host does not access EF. Native AOT hosts must set `IncludeEfCore=false`; a runtime-feature
guard removes that reflection-based path at link time and rejects attempts to enable it at runtime. ASP.NET request spans cover proxy and JWKS requests, while
HttpClient instrumentation covers downstream calls; there is no separate custom transform span.
The gateway remains in the Native AOT audit. Consumers that reference the gateway package in
their own host choose their own observability registration.

### Key span names to recognize at a glance

| Span name | Source | Meaning |
|-----------|--------|---------|
| `Mediator.Send {Type}` | `Kun.Mediator` | A command or query was dispatched |
| `outbox.publish` | `Kun.EventBus.Outbox` | An outbox row was published to the broker |
| `grpc.server` | `Kun.Grpc` | A unary or streaming gRPC call was handled |
| `saga.step.execute` | `Kun.Saga` | A saga step ran forward |
| `saga.step.compensate` | `Kun.Saga` | A saga step ran in compensation |
