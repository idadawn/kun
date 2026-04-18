<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.EventBus

## Purpose

Event-bus abstractions: integration event base (`IIntegrationEvent`), event-type registry (`IEventTypeRegistry` — maps CLR types to stable IDs like `order.placed.v1`), Outbox schema and publisher background service contract, `IIntegrationEventBus` transport-agnostic publish interface, and the `IdempotentConsumerDecorator` that wraps consumer handlers.

## Key Files

| File | Description |
|------|-------------|
| `Kun.EventBus.csproj` | `IsAotCandidate=true`, references `Kun.Core` |

## For AI Agents

### Working In This Directory

- **Stable event IDs are the critical design choice.** Event types are identified by strings of shape `{bounded-context}.{event-name}.v{version}` (e.g. `order.placed.v1`, `payment.refunded.v2`) — never by `AssemblyQualifiedName`. The registry persists these mappings.
- **AOT-safe serialization only**: integration event payloads are serialized via `KunJsonContext.Default.GetTypeInfo(type)!`. Every integration event type must be registered in `KunJsonContext`.
- **The Outbox contract** (this module defines the shape, `Kun.EntityFrameworkCore` writes, `Kun.EventBus.RabbitMQ` publishes): message row carries `Id`, `EventTypeId` (stable string), `Payload` (JSON), `TraceParent`/`TraceState` (W3C), `OccurredAt`, `PublishedAt`, `Status`.
- **Consumer idempotency** is provided by `IdempotentConsumerDecorator<T>` backed by `IProcessedMessageStore`. Consumers written by users are plain `IIntegrationEventHandler<T>` — the decorator is applied at DI registration time.
- **Version evolution**: when an event's payload changes shape, **always bump the `.v{n}` suffix** and add a new type rather than modifying the v1 type — older messages still in flight must deserialize correctly.

### Testing Requirements

Publisher correctness (trace context propagation, retry on transient failure) is covered by `tests/Kun.IntegrationTests`. Unit tests of `IEventTypeRegistry` registration and lookup belong close to this module.

### Common Patterns

- `IEventTypeRegistry.Register<TEvent>("stable-id")` called once per event type during startup.
- Domain events (`IDomainEvent`, defined in `Kun.Core`) never cross process boundaries — they are translated to integration events inside the publishing service.

## Dependencies

### Internal

- `Kun.Core` — `IDomainEvent`, `IIntegrationEvent`, `ITenantContext`.

### External

- `Microsoft.Extensions.Hosting.Abstractions` (for `BackgroundService`).
- `Microsoft.Extensions.Logging.Abstractions`.

<!-- MANUAL: -->
