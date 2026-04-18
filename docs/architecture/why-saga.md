# Why Kun Uses Saga Orchestration

Distributed transactions cannot use database-level locking across service boundaries. Sagas
replace the two-phase commit with a sequence of local transactions, each of which can be
compensated if a later step fails. Kun implements this as Orchestration — a central coordinator
rather than services reacting to each other's events. This document explains the choice and what
it costs.

---

## The problem: multi-step distributed work

Consider an order placement flow: reserve inventory in Catalog, charge the customer via Payment,
hand off to Shipping, then notify the customer. Each step touches a different service and a
different database. A failure at step 3 means steps 1 and 2 have already committed and must be
explicitly reversed. Without a coordinator, determining what to reverse and in what order requires
every service to understand the global workflow — which it cannot do without coupling.

---

## Orchestration vs. Choreography

**Choreography** places no central coordinator. Each service publishes events and reacts to
events from others. The workflow is implicit in the event topology. This works well for simple,
stable flows but becomes difficult to debug when something goes wrong: tracing a failure requires
reconstructing the event sequence across multiple services and topics, and adding a step means
modifying the event contracts of multiple services simultaneously.

**Orchestration** places a central coordinator — in Kun, `SagaOrchestrator` — that knows the
complete step list and drives execution. Each service executes a step when instructed and reports
success or failure. The coordinator decides what comes next.

Kun chose Orchestration for three concrete reasons:

1. **Failure recovery is explicit.** When step `N` fails, the orchestrator knows it must
   compensate steps `N-1` down to `0` in reverse order. This logic lives in one place — the
   `ISaga<TContext>` definition — rather than being inferred from event reactions scattered across
   services.

2. **State is inspectable.** Every `SagaInstance` is a row in the `saga_instances` table with a
   `State` column (`Running`, `Compensating`, `Completed`, `Compensated`, `Failed`) and a
   `CurrentStepIndex`. A plain SQL query tells you exactly where every in-flight saga is. In
   choreography the equivalent requires correlating event logs from multiple topics.

3. **Compensation order is deterministic.** Kun's `SagaStepExecutor` compensates from the failed
   step index backwards, one entry per `saga_step_queue` row. The order is guaranteed by the
   queue, not by the timing of event delivery.

---

## The three recovery paths

**Happy path.** `SagaStepExecutor` polls `saga_step_queue`, calls `ExecuteAsync` on each entry,
and on success enqueues the next step. When the last step succeeds the instance transitions to
`Completed`.

**Failure compensation.** When `ExecuteAsync` throws `SagaStepFailedException`, the executor
transitions the instance to `Compensating` and enqueues `CompensateAsync` calls from the failed
step index back to step 0. Compensation runs through the same polling loop. The instance ends as
`Compensated`.

**Timeout compensation.** `SagaTimeoutDetector` scans every 5 seconds for instances where
`TimeoutAt < UtcNow AND State = Running`. On a match it calls `SagaInstance.StartCompensating`
and enqueues the same reverse entries the failure path would produce. In the Order sample,
`ShipOrderStep` has a 10-second timeout override; the saga default is 30 seconds.

---

## Idempotency is mandatory

Every `CompensateAsync` must be safe to call more than once. The `saga_step_queue` table tracks
`ProcessedAt` to prevent the executor from processing the same entry twice, but a crash between
processing and marking can cause a re-run. A compensation that checks aggregate state before
acting — "cancel the order only if its status is `Paid`" — is idempotent. One that unconditionally
issues a refund is not. The same requirement applies to `ExecuteAsync`.

The Order sample (`samples/Kun.Sample.Order`) is the concrete reference: `PlaceOrderCommandHandler`
calls `ISagaOrchestrator.StartAsync<OrderCreationSaga, OrderCreationSagaContext>()`, which
persists the `SagaInstance` and the first queue entry in the same EF Core transaction as the
`Order` row. The saga cannot be partially started.

---

## Trade-offs to be aware of

**Coupling to the coordinator.** Every step must be registered with the orchestrator. The step
list in the `ISaga<TContext>` definition couples the coordinator to each participant's expected
behavior. You cannot add a participant without modifying that list.

**State must be modeled explicitly.** The saga context carries all shared state as a serializable
payload. Anything a step needs from a previous step must be stored there — more work than implicit
event-driven state, but also visible and auditable.

**No distributed lock across step execution.** The executor relies on `SKIP LOCKED` to avoid
races between instances. If your database does not support `SKIP LOCKED` (PostgreSQL does;
older MySQL does not), step idempotency is the only safety net.

Orchestration adds a coordinator as a point of coupling. In exchange it gives you deterministic
failure recovery, a single inspectable state table, and no ambiguity about compensation order.
