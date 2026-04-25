<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.Audit.EntityFrameworkCore

## Purpose

EF Core 10 adapter for `Kun.Audit`. Provides:

- `AuditLogConfiguration` — `IEntityTypeConfiguration<AuditLog>` with
  tenant + action + timestamp indexes and immutability conventions.
- `AuditSaveChangesInterceptor` — EF Core `SaveChangesInterceptor` that
  walks `ChangeTracker.Entries()` before `SaveChanges`, buffers one
  `AuditLog` per tracked aggregate / auditable entity, then forwards the
  buffer to the registered `IAuditWriter` after the business transaction
  commits.
- `EfAuditWriter` — `IAuditWriter` that persists entries through a
  consumer-supplied audit `DbContext` (the same `KunDbContext` or a
  dedicated audit database).
- `AddKunAuditEntityFrameworkCore` DI extension.

## Layer and AOT

- Layer: **Infrastructure** (depends on `Kun.Audit` + `Kun.EntityFrameworkCore`).
- `<IsAotCandidate>false</IsAotCandidate>` — trim-safe only, matching
  `Kun.EntityFrameworkCore`. Consumers running full native AOT must use
  an alternative writer (e.g. `EventBusAuditWriter` from `Kun.Audit`).
- Decision recorded in ADR-003.

## Indexes & immutability

The entity configuration creates:

| Index | Purpose |
|-------|---------|
| `(tenant_id, timestamp DESC)` | primary query pattern — per-tenant activity feeds |
| `(tenant_id, actor_id, timestamp DESC)` | per-user drill-down |
| `(tenant_id, action, timestamp DESC)` | compliance report (e.g. all `user.login.*` events) |
| `(tenant_id, resource_type, resource_id)` | resource history views |

The table is **append-only** by convention. The interceptor never
updates or deletes existing rows. Databases that support it should
enforce immutability at the DB layer (PostgreSQL: revoke `UPDATE` /
`DELETE` on the table from the application role; migrations go through
an admin role). Triggers / hash-chain tamper detection are deferred —
see ADR-003 §Future Work.

## Wiring

```csharp
services.AddKunAudit()                             // abstractions + Null fallback
    .AddKunAuditEntityFrameworkCore()              // registers the interceptor
    .UseEntityFramework<AppDbContext>();           // swap in EfAuditWriter<AppDbContext>

// In your DbContext registration:
services.AddDbContext<AppDbContext>((sp, opts) =>
{
    opts.UseNpgsql(connectionString)
        .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});

// In your DbContext.OnModelCreating:
modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
```

## For AI Agents

- Do **not** lift types from here back into `Kun.Audit`. The EF
  dependency is the whole reason this module exists.
- The interceptor uses `SavingChangesAsync` + `SavedChangesAsync`
  hooks — captures change metadata before save, forwards to the writer
  after. Keeping the writer call post-save avoids the "audit row leaked
  because business txn failed" footgun.
- No reflection — all traversal goes through `ChangeTracker.Entries()`
  and typed property getters on Kun.Core marker interfaces
  (`IAggregateRoot`, `IMultiTenant`, `IAuditedObject`).
- Payload JSON is built with `Utf8JsonWriter` (AOT-safe even though the
  module is not currently AOT-candidate — keeps hoisting to an
  AOT-safe module cheap if EF Core AOT matures).
