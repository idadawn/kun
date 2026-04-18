<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Domain

## Purpose

DDD domain building blocks. `Entity<TKey>`, `AggregateRoot<TKey, TUserKey>` (with convenience `AggregateRoot<TKey>` defaulting audit-user to `Guid`), `ValueObject` with component-based equality, `IDomainEvent`, `IIntegrationEvent`, and cross-cutting marker interfaces (`ISoftDelete`, `IAuditedObject<TUserKey>`, `IMultiTenant`). No infrastructure, no attributes, no ORM references — these types are consumed by every service built on Kun.

## Key Files

| File | Description |
|------|-------------|
| `AggregateRoot.cs` | Aggregate base with protected `AddDomainEvent`, `ClearDomainEvents`, virtual soft-delete |
| `Entity.cs` | `Entity<TKey>` with ID-based equality; transient (default-ID) entities are never equal |
| `ValueObject.cs` | Abstract `ValueObject` with `GetEqualityComponents()` override pattern |
| `IAggregateRoot.cs` | Non-generic + generic marker interfaces used by `ChangeTracker` filtering |
| `IDomainEvent.cs` | Marker for in-process, same-aggregate events |
| `IIntegrationEvent.cs` | Marker for cross-service events delivered via Outbox + message bus; carries `EventId` for idempotency |
| `Auditing.cs` | `ISoftDelete`, `IAuditedObject`, `IAuditedObject<TUserKey>`, `IMultiTenant` |

## For AI Agents

### Working In This Directory

- **No attributes on these types.** Any `[Table]` / `[Key]` / `[Column]` / `[Required]` / `[JsonPropertyName]` belongs in the infrastructure layer (`IEntityTypeConfiguration<T>` in `Kun.EntityFrameworkCore`, validators in `Kun.Validation`).
- **`AggregateRoot` setters are `protected`** — external modification must go through business methods that validate invariants and raise domain events.
- **`Entity.Equals` treats two default-ID entities as unequal.** This avoids accidental "two new-but-unsaved entities are the same" bugs.
- **`ValueObject.GetEqualityComponents()`** is the one method subclasses must override — it yields each component in a stable order.
- **`IDomainEvent` vs `IIntegrationEvent`**: domain events are in-process only and raised from within aggregates; integration events are cross-service and only produced when translating a domain event at the outbox boundary.
- **`TUserKey` is generic** so audit fields accept `Guid` (local users), `string` (IdP `sub`), or `long` (DB identity) without forcing all services to align.

### Testing Requirements

Tests for these types live in `tests/Kun.Core.Tests`. Cover: aggregate invariant enforcement, value-object equality for equal/unequal/reordered components, transient-entity non-equality, soft-delete sets `IsDeleted` + `DeletedAt`.

### Common Patterns

- Aggregates raise events inside business methods:
  ```csharp
  public void Place() {
      if (Status != OrderStatus.Draft) return;
      Status = OrderStatus.Placed;
      AddDomainEvent(new OrderPlacedEvent(Id, DateTime.UtcNow));
  }
  ```
- Value objects are immutable — changes return new instances (`money.Add(other) ⇒ new Money(...)`).

## Dependencies

### Internal

None — purely BCL-backed.

<!-- MANUAL: -->
