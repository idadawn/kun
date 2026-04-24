# ADR-015: Outbox pattern design for `Kun.EventBus`

**Status**: Accepted (retroactive)
**Date**: 2026-04-24
**Author**: claude-kun (Kun framework specialist)
**Extends**: `docs/architecture/why-outbox.md` (primer)
**Supersedes**: none

**Context**: Integration events fan out to downstream services over
RabbitMQ. A naive implementation publishes during the write-side
handler — which either (a) publishes before commit (downstream sees
an event for a state that never existed if the transaction rolls
back), or (b) publishes after commit (downstream never sees an
event if the app crashes between `SaveChanges` and `Publish`). The
Outbox pattern closes both failure modes by persisting a message
row *inside* the write transaction and publishing asynchronously.

Kun ships the pattern end-to-end, but the rollout predates the ADR
index. `kun/CLAUDE.md` §Outbox has the normative rules; this ADR
walks through the design so new contributors can reason about the
type surface without reverse-engineering it.

---

## Decision

### Type surface

All abstractions live in `Kun.EventBus`; the persistence half
lives in `Kun.EntityFrameworkCore`. Two modules because:

1. `Kun.EventBus` must stay AOT-candidate and take no EF Core
   dependency — the publisher loop and the consumer idempotency
   machinery do not need EF Core.
2. `Kun.EntityFrameworkCore` is the one place we accept non-AOT
   trade-offs (ADR-004 trail), and atomic enqueue+save *has* to
   run inside `DbContext.SaveChangesAsync`.

| Namespace / type | Role |
|---|---|
| `Kun.EventBus.Outbox.OutboxMessage` | Persisted row: `Id`, `EventTypeId`, `Payload`, `Status`, `TraceParent`, `TraceState`, retry counters. |
| `Kun.EventBus.Outbox.OutboxStatus` | `Pending` / `Published` / `Failed`. Enum. |
| `Kun.EventBus.Outbox.IIntegrationEventOutbox` | Consumer-facing append API: `EnqueueAsync(IIntegrationEvent, CancellationToken)`. Called from handler code. |
| `Kun.EventBus.Outbox.IOutboxWriter` | Low-level write primitive — used by the EF Core accumulator and by any hand-rolled `IIntegrationEventOutbox` implementation. |
| `Kun.EventBus.Background.IOutboxMessageStore` | Read / update store the publisher pump talks to (`ReadPendingBatchAsync`, `MarkPublishedAsync`, `MarkFailedAsync`). |
| `Kun.EventBus.Background.IOutboxLockProvider` | Distributed lease so multiple pods don't double-publish. `TryAcquireAsync(lockName, ttl, ct)` returns `IAsyncDisposable?`; `null` means another instance holds it. |
| `Kun.EventBus.Background.OutboxPublisherBackgroundService` | Hosted service running the read → lock → publish → mark loop. |
| `Kun.EventBus.Background.OutboxPublisherOptions` | Batch size, poll interval, lease TTL, failure backoff. |
| `Kun.EventBus.Background.NullOutboxLockProvider` | Single-pod default — always succeeds. |
| `Kun.EventBus.Background.RedisOutboxLockProvider` | Multi-pod production default — SET NX + EX. |
| `Kun.EventBus.Background.Internal.ProcessedMessageStore` | Consumer-side "seen before?" cache backing `IdempotentConsumerDecorator<T>` (ADR-003 trail). |
| `Kun.EntityFrameworkCore.IntegrationEventOutboxAccumulator` | Collects aggregate-produced `IIntegrationEvent`s during `SaveChangesAsync` and writes `OutboxMessage` rows in the same transaction. |
| `Kun.EntityFrameworkCore.OutboxMessageConfiguration` | `IEntityTypeConfiguration<OutboxMessage>` — table name, column types, partial index on `Status = Pending` + `CreatedAt` ordering. |

### Atomicity boundary

The contract is: **Outbox enqueue + aggregate save commit together
or neither.** Enforced by
`IntegrationEventOutboxAccumulator.AccumulateBeforeSave` hooked into
`KunDbContext.SaveChangesAsync`:

1. `ChangeTracker.Entries<IAggregateRoot>()` gives all tracked
   aggregates.
2. For each, drain `DomainEvents` → convert to `IIntegrationEvent`
   via the domain-event handler chain → materialise as
   `OutboxMessage` rows on the same `DbContext`.
3. `ClearDomainEvents()` on every aggregate so replay doesn't
   double-enqueue.
4. The single `SaveChangesAsync` call writes both aggregate changes
   and outbox rows in one EF Core transaction.

There is **no** "open transaction, publish, commit, close" dance.
The publish is asynchronous and happens in a separate process
(background service), decoupled from the write-side transaction
by the persisted outbox row.

### Publisher pump

`OutboxPublisherBackgroundService` runs in every hosting process
with the EventBus registered. On each tick:

1. `lockProvider.TryAcquireAsync("outbox-publisher", leaseTtl, ct)`.
   If `null`, sleep and retry — another pod owns the batch.
2. `store.ReadPendingBatchAsync(options.BatchSize, ct)` — reads
   `BatchSize` oldest `Pending` rows. The partial index on
   `(Status = Pending, CreatedAt ASC)` keeps this O(batchsize)
   per tick even at 10⁶ total rows.
3. For each row: publish to the broker (RabbitMQ by default; a
   null publisher exists for tests). On success →
   `MarkPublishedAsync`. On exception → `MarkFailedAsync` +
   increment retry counter. Hit max retries → leave status
   `Failed` for operator triage; log + metric.
4. Release the lease (dispose the `IAsyncDisposable`).

Backoff + jitter on the outer loop when the lease is contended or
the batch is empty. Configurable via `OutboxPublisherOptions`.

### Consumer idempotency

Publishing is **at-least-once**. Consumers `IdempotentConsumerDecorator<T>`
wraps every `IIntegrationEventHandler<T>`; first run writes the
stable event ID into `IProcessedMessageStore` (Redis
`SETNX + EX` keyed on `{eventTypeId}:{eventId}`). Subsequent
deliveries short-circuit before the handler body. See ADR trail
for `Kun.EventBus.IntegrationEvent` idempotency; this ADR only
states the contract: **handlers must be safe to re-invoke** and
the framework will make the decorator path cheap for them.

### W3C trace propagation

`OutboxMessage.TraceParent` + `TraceState` capture
`Activity.Current?.Id` / `TraceStateString` at enqueue. The
publisher restores them on the outgoing envelope (`traceparent`
header) so the downstream consumer joins the original trace when
it starts its own activity via `ActivitySource.StartActivity` with
the parent id from the header.

**Note** — this is *trace context* propagation (W3C Trace Context
spec), not *baggage* propagation (ADR-013). Baggage is ambient
cross-cutting data (tenant / user / outbox-message-id)
deliberately mirrored onto spans and logs by the enricher; trace
context is *just the trace ID chain*. Kun keeps the two concepts
separate so that baggage can be audited / stripped at system
boundaries without affecting trace correlation.

### Stable event IDs — format and registry

All integration events are tagged with a stable string identifier
`"{bounded-context}.{name}.v{n}"` — e.g. `order.placed.v1`,
`billing.invoice.paid.v2`. Two non-negotiables:

- **Never** `Type.AssemblyQualifiedName`. Renaming a CLR type or
  moving it across assemblies breaks every persisted row. The
  stable ID is the wire contract.
- **Versioned suffix** (`v1`, `v2`, …) is bumped on breaking payload
  changes. Non-breaking additions (new optional property) keep
  the version.

`IEventTypeRegistry` resolves `eventTypeId ↔ CLR Type` at runtime.
Consumers register via `IEventTypeRegistrar` in their DI
composition root; missing registrations fail loudly on publisher
startup rather than silently at publish time.

---

## What Outbox does **not** do

- **Read-side projections / read models.** Those belong in CQRS
  Query handlers (`Kun.Dapper`). Outbox is the write-side egress.
- **Saga orchestration.** `Kun.Saga` ADR-S01 is the orchestrator;
  Saga steps use integration events for cross-service handoffs but
  the orchestration state lives in its own table.
- **Command routing.** Commands go through `Kun.Mediator`. Outbox
  carries *events* only.
- **Snapshotting / rehydration.** Outbox is for *outgoing*
  messages. Inbound replay + aggregate rehydration is the read-
  side concern.

---

## Alternatives considered

### Direct publish inside the handler

Rejected — either breaks atomicity (publish before commit) or
loses messages (publish after commit + crash). See §Context.

### Change Data Capture (Debezium / Postgres logical replication)

Captures state changes, not semantic intents. You get *row
changed* messages; the consumer then has to reverse-engineer
"what business event happened?" Kun's integration events carry
semantics (`OrderPlaced`, not `INSERT orders VALUES (…)`), so a
CDC approach would need a translator layer that introduces the
same complexity as Outbox without its atomicity advantages.
Revisit only if an explicit data-lake / audit replay need appears.

### Transactional outbox tables per-bounded-context

Each bounded context gets its own table. Considered, rejected —
no concrete benefit in the single-DB-per-service layout we run
today; one shared `__outbox_messages__` table with
`EventTypeId` prefixing the bounded context is simpler and the
partial-index-on-`Pending` query cost is identical. Revisit if a
service needs per-context publishing priorities.

### Kafka source + compacted topics as ledger

Moves the atomicity problem to the storage layer (Kafka-backed
state store). Operationally heavier than Postgres for the current
scale and mixes write-authority (DB) with event-authority (Kafka).
Deferred indefinitely.

---

## Impact on existing code

None — this ADR is retroactive. The types described are the
types that have been shipping since Phase-1 M2. Cross-links to
this ADR land as part of KN-SELF-011 AC-2.

## Follow-ups

- **F1**: Dead-letter handling — `Failed`-status rows above retry
  cap today sit in the table forever; needs a janitor job or a
  dedicated DLQ table. Tracked as backlog item.
- **F2**: Per-tenant batch priority — some tenants' events are
  time-sensitive (webhook SLAs). Weight-based batch selection
  sits outside the current `ReadPendingBatchAsync`.
- **F3**: Outbox-specific test helpers in
  `Kun.Testing.EntityFrameworkCore` (ADR-012 §F1 referenced this
  as a companion module).
