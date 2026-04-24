# Why Kun Uses the Outbox Pattern

When a service saves a business entity and publishes an integration event, those are two separate
I/O operations. The Outbox pattern exists to make them behave as one. This document explains the
problem, Kun's solution, and the trade-offs you accept.

---

## The dual-write problem

Consider the naive approach to event publishing:

```csharp
await dbContext.SaveChangesAsync();          // (1) aggregate persisted
await broker.PublishAsync(integrationEvent); // (2) event published to RabbitMQ
```

These two operations are not atomic. Any failure between (1) and (2) leaves the system
inconsistent: the database reflects the new state but consumers never learn about it. Retrying
(1) is unsafe — it may double-write. Putting (2) before (1) has the mirror problem: the event
fires but the database write fails.

Worse, the broker itself is a network dependency. A transient RabbitMQ outage at step (2) means
lost events with no recovery path — unless you build one yourself. That recovery logic is exactly
what the Outbox pattern provides.

---

## How Kun atomizes the publish

Kun's write-side pipeline centers on `EfUnitOfWork.SaveChangesAsync`. The sequence inside that
method is:

1. Domain events are collected from `ChangeTracker.Entries<IAggregateRoot>()` and dispatched
   in-process via `IDomainEventDispatcher`.
2. In-process domain event handlers call `IIntegrationEventBus.Publish`, which stages the
   integration event in the `IntegrationEventBuffer` — an in-memory accumulator scoped to the
   current request.
3. Each staged event is converted to an `OutboxMessage` row. The stable event type identifier
   (e.g. `product.published.v1`) is resolved from `IEventTypeRegistry` — never from
   `Type.AssemblyQualifiedName`, which would break across assembly renames and refactors.
4. The payload is serialized via `KunJsonContext.Default.GetTypeInfo(type)`, which uses a
   source-generated `JsonSerializerContext`. No runtime reflection; safe for AOT.
5. `Activity.Current?.Id` and `Activity.Current?.TraceStateString` are captured as `TraceParent`
   and `TraceState` on the message so the trace can be resumed on the other side of the broker.
6. `DbContext.SaveChangesAsync` commits the aggregate changes and the `OutboxMessage` rows in a
   single database transaction.

Because step 6 is one transaction, either both the business state and the Outbox row are
committed, or neither is. The dual-write problem is eliminated.

---

## How the background publisher drains the Outbox

`OutboxPublisherBackgroundService` is a `BackgroundService` that runs in every process that has
the Outbox registered. On each scan cycle it:

1. Queries `OutboxMessage` rows with `Status = Pending`, ordered by `OccurredAt`.
2. For each message, restores the W3C trace context from `TraceParent`/`TraceState` to link the
   publish span to the original request trace.
3. Calls `IIntegrationEventPublisher.PublishAsync` — in production this is the RabbitMQ
   publisher from `Kun.EventBus.RabbitMQ`; in the Order sample it is a no-op that logs to
   stdout.
4. On success: calls `OutboxMessage.MarkPublished` and saves.
5. On failure: increments `AttemptCount`, records `LastError`, and retries with exponential
   back-off. After the configured maximum attempts the message transitions to `Failed` and
   requires manual intervention.

The scan uses `SELECT ... FOR UPDATE SKIP LOCKED` (or a Redis distributed lock) so multiple
instances of the same service do not race on the same batch.

---

## Consumer idempotency via `IProcessedMessageStore`

Because the publisher uses at-least-once delivery — the broker acknowledgement and the database
`MarkPublished` update are still two operations — a consumer may receive the same message more
than once. Every consumer handler is wrapped in `IdempotentConsumerDecorator<T>`, which checks
`IProcessedMessageStore` before invoking the handler.

The default implementation uses Redis `SETNX` with a TTL (default 30 minutes) keyed on
`idemp:{consumerName}:{messageId}`. If the key already exists the message is acknowledged and
discarded. Consumer handlers must still be written to be safe when called more than once, but the
decorator handles the common case without requiring every handler to implement its own
deduplication.

---

## Trade-offs to be aware of

**Latency is measured in seconds, not milliseconds.** The background publisher runs on a polling
interval. A consumer will see an event seconds after the originating transaction commits, not
sub-millisecond. If your design requires synchronous cross-service consistency, the Outbox is not
the right tool — gRPC calls with `Result`-based error handling are.

**The Outbox store requires a reliable database.** The pattern works because the database is the
source of truth for pending messages. If the database is unavailable, no messages are published —
but this is safer than losing messages silently. Recovery is automatic once the database is
reachable again.

**`Failed` messages need a recovery workflow.** Messages that exhaust retries end up in the
`Failed` state. There is no automatic dead-letter re-queue for failed Outbox messages; you need
an operational process to inspect, correct, and republish them.

The Outbox pattern adds operational surface area in exchange for a guarantee that the dual-write
problem cannot silently corrupt your system state. That trade is almost always worth making in a
distributed system where correctness matters.

---

## See also

- [ADR-015 Outbox pattern design](./ADR-015-Outbox-pattern.md) — full design walk-through: type
  surface, atomicity boundary, publisher pump, consumer idempotency, rejected alternatives.
- `kun/CLAUDE.md` §Outbox — normative rules (atomicity, stable event IDs, idempotent consumers,
  W3C trace propagation). The rules constrain; this primer explains the problem; the ADR explains
  the design.
