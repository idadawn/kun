<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.EventBus.RabbitMQ

## Purpose

RabbitMQ transport implementation of `IIntegrationEventBus`. Publishes Outbox-drained messages to RabbitMQ exchanges, attaches W3C Trace Context headers so consumers resume the distributed trace, and provides a consumer host that dispatches incoming messages through the `IdempotentConsumerDecorator` to user handlers.

## Key Files

| File | Description |
|------|-------------|
| `Kun.EventBus.RabbitMQ.csproj` | `IsAotCandidate=true` |

## For AI Agents

### Working In This Directory

- **Trace context headers are mandatory**: on publish, copy `TraceParent`/`TraceState` from the Outbox row into the RabbitMQ `BasicProperties.Headers`. On consume, restore them into `Activity.Current` via `ActivitySource.StartActivity(parentId: ...)` so the trace chain doesn't break at the queue boundary.
- **Stable event IDs live on the wire**: the envelope's `EventTypeId` header carries the string ID; consumer lookup uses that, not CLR types or type names.
- **Consumer idempotency is not optional** — the `IdempotentConsumerDecorator` from `Kun.EventBus` must wrap every consumer. Outbox delivery is at-least-once.
- **Serialization**: `KunJsonContext.Default.GetTypeInfo(eventType)!` — do not introduce `JsonSerializer.Serialize(obj)` without explicit type info.
- RabbitMQ.Client 7.x has async-native APIs; use them — do not call `.Result` / `.Wait()`.

### Testing Requirements

Use `Testcontainers.RabbitMq` in `tests/Kun.IntegrationTests`. Validate trace-context round-tripping, at-least-once delivery, and idempotent consumer behavior under redelivery.

### Common Patterns

- Exchange type is `topic`; routing key = stable event ID (e.g. `order.placed.v1`).
- Consumer queues are named `{service}.{event-type-id}` so multiple services can subscribe to the same event.

## Dependencies

### Internal

- `Kun.EventBus` — transport contract, Outbox schema, `IdempotentConsumerDecorator`.

### External

- `RabbitMQ.Client` 7.x

<!-- MANUAL: -->
