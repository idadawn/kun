<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.Audit

## Purpose

Audit-log abstractions at the **Abstraction layer**. Contains `IAuditWriter`,
`IAuditReader`, `AuditableAttribute`, and the `AuditLog` POCO. Zero
infrastructure dependencies — EF Core integration lives in the sibling
`Kun.Audit.EntityFrameworkCore` module; integration-event fan-out is provided
by `EventBusAuditWriter` which depends only on `Kun.EventBus`.

## Layer and AOT

- Layer: **Abstraction** (depends on `Kun.Core` + `Kun.EventBus` only)
- `<IsAotCandidate>true</IsAotCandidate>` — strict AOT.
- Reflection is forbidden. JSON serialization for `AuditLog` integration
  events must go through a caller-provided `JsonTypeInfo` registered in the
  consumer's `JsonSerializerContext`.

## Key Types

| Type | Purpose |
|------|---------|
| `AuditLog` | POCO record persisted / published per audit event. Implements `IMultiTenant` so it participates in `KunDbContext` tenant filters when stored inside a `KunDbContext`-derived context. |
| `IAuditWriter` | Abstraction for persisting / publishing an audit log entry. |
| `IAuditReader` | Abstraction for querying audit log entries (pagination + filter). |
| `AuditableAttribute` | Declarative marker for Command / endpoint handlers that want a canonical `action` / `resourceType` emitted. The attribute is consumed by the Mediator pipeline behavior in consumer code — this module only declares it. |
| `NullAuditWriter` | Default writer; drops entries. Registered when consumers don't explicitly configure a backend so that interceptor wiring stays safe. |
| `EventBusAuditWriter` | Opt-in writer that stages the `AuditLog` as an integration event via `IIntegrationEventBus`. Requires the consumer to register `kun.audit.record.v1` with `IEventTypeRegistry`. |

## Identifiers (stable)

- Integration event type: `kun.audit.record.v1`
- Actor type constants: `user`, `system`, `service` (see `AuditActorType`).

## Wiring

```csharp
services.AddKunAudit(options =>
{
    options.DefaultActorType = AuditActorType.User;
    options.CaptureEntityPayload = true;
});

// Opt-in fan-out via Outbox / EventBus:
services.AddKunAudit().UseEventBus();

// EF Core interceptor (separate module):
services.AddKunAuditEntityFrameworkCore();
```

## For AI Agents

- Don't add EF Core references here — EF concerns go in
  `Kun.Audit.EntityFrameworkCore`. Layering is enforced by CI.
- New members on `IAuditWriter` / `IAuditReader` are **public API
  changes** — update `PublicAPI.Unshipped.txt` when adding.
- Keep `AuditLog` as a POCO with no ORM / JSON attributes. Persistence
  mapping lives in `Kun.Audit.EntityFrameworkCore/AuditLogConfiguration.cs`.
- AOT rule: no `JsonSerializer.Serialize(obj)` without a `JsonTypeInfo`.
  The abstractions accept already-serialized `PayloadJson` strings so the
  serialization decision belongs to the interceptor / consumer.
