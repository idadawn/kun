# ADR-003: Introduce `Kun.Audit` and `Kun.Audit.EntityFrameworkCore` Modules

**Status**: Accepted
**Date**: 2026-04-22
**Author**: claude-kun (Kun framework specialist)
**Context**: `neuroagent-phase1-gap.md` §2 flagged audit logging as missing for P1-CS-019. An initial attempt by the business-services pane (glm) to build the feature inside `neuroagent-csharp` stalled at the framework boundary: per the 路由边界铁律 in the project `CLAUDE.md`, cross-cutting capabilities that could be reused by other services must land in `kun/`, not as a private fork inside a consumer repository. Handoff `P1-KN-Audit-handoff.md` captured the expected deliverables.

---

## Decision

Introduce **two** new framework modules:

| Module | Layer | `IsAotCandidate` | Responsibility |
|--------|------|:----------------:|----------------|
| `Kun.Audit` | Abstraction | `true` | `IAuditWriter` / `IAuditReader` / `AuditableAttribute`, `AuditLog` POCO, `NullAuditWriter`, `EventBusAuditWriter`, `AddKunAudit` DI |
| `Kun.Audit.EntityFrameworkCore` | Infrastructure | `false` | `AuditSaveChangesInterceptor`, `AuditLogConfiguration`, `EfAuditWriter<TDbContext>`, `UseEntityFramework<TDbContext>()` DI |

The original handoff brief (P1-KN-Audit-handoff.md) specified a single
`Kun.Audit` module with `IsAotCandidate=true`. That specification is
incompatible with the four-layer / AOT rules recorded in
`CLAUDE.md` §Architectural constraints — EF Core 10 is not yet fully AOT
compatible, which is why `Kun.EntityFrameworkCore` itself ships with
`IsAotCandidate=false`. Bundling EF Core types into an AOT-candidate
module would fail `dotnet publish -p:PublishAot=true /warnaserror` at
CI. Per the "Stop gate" rule on AOT warnings, this ADR records the
split decision instead of silently flipping `IsAotCandidate=false` on
the single-module variant.

---

## Decision Drivers

1. **Layering rule** — Abstractions must not depend on EF Core. Putting the
   interceptor (an EF Core type) in the same csproj as the
   `IAuditWriter` contract would elevate EF Core into the Abstraction
   layer and cascade a transitive EF Core dependency onto every
   consumer of `Kun.Audit`.
2. **AOT strategy** — `Kun.Mediator`, `Kun.Core`, `Kun.EventBus` etc. are
   AOT-strict and CI enforces zero AOT warnings. EF Core 10's incomplete
   AOT support means any module that references EF Core types cannot be
   AOT-strict. Split preserves the option for full-AOT consumers to use
   `EventBusAuditWriter` (abstraction-only, AOT-safe) while still offering
   `EfAuditWriter<TDbContext>` to trim-safe deployments.
3. **Composability** — Consumers pick exactly one writer via the
   `KunAuditBuilder` fluent chain: `UseEventBus()` or
   `UseEntityFramework<TDbContext>()`. Additional writers (SIEM forwarder,
   hash-chain tamper-evidence, file append-log) can be added as further
   `UseX()` extensions on `KunAuditBuilder` without touching core.
4. **Zero-config safe default** — A `NullAuditWriter` is registered by
   default so that a consumer who only calls `AddKunAudit()` (without
   picking a backend) doesn't crash at first use. It logs a single
   warning so operators notice the misconfiguration without log-flood.

---

## Alternatives Considered

| Alternative | Description | Why Rejected |
|-------------|-------------|--------------|
| A. Single `Kun.Audit` module with EF Core inside | Matches handoff text literally; minimises module count | Violates layering + AOT rules. AOT publish would fail at CI; consumer of `IAuditWriter` abstraction would transitively pull EF Core. |
| B. Single `Kun.Audit` with `IsAotCandidate=false` | Keeps module count low; drops AOT promise for audit users | Eliminates the AOT-friendly fan-out path (`EventBusAuditWriter`) — consumers running full AOT would have no audit option at all. |
| C. Fold into `Kun.EntityFrameworkCore` | Reuse the existing infrastructure module; no new csproj | Audit is a distinct concern with its own versioning cadence, own pipeline behavior surface, and its own consumer (compliance team). Bloating `Kun.EntityFrameworkCore` makes the dependency graph coarser than necessary. |
| D. **Split into Abstraction + Infrastructure (selected)** | Two new csproj entries; mirrors `Kun.EventBus` + `Kun.EventBus.RabbitMQ` pattern that is already established | Slight module-count overhead, offset by preserving layering & AOT promises and giving consumers a clean pick-one-writer API. |

---

## Why Chosen

Option D is the only alternative that respects both the layering
four-layer flow and the AOT-candidate invariant. It also matches the
existing `Kun.EventBus` → `Kun.EventBus.RabbitMQ` pattern, so there is
no new architectural convention to learn.

---

## Integration Points

```
┌──────────────────────────┐
│  Consumer business code  │                 emits IAggregateRoot / IMultiTenant
└──────────────────────────┘
           │
           ▼
┌──────────────────────────┐
│  AppDbContext (consumer) │         ← AddInterceptors(AuditSaveChangesInterceptor)
└──────────────────────────┘
           │ SaveChangesAsync
           ▼
┌──────────────────────────────────────────────┐
│  AuditSaveChangesInterceptor                 │  (Kun.Audit.EntityFrameworkCore)
│    SavingChangesAsync:  walk ChangeTracker,  │
│                         buffer PendingAudit  │
│    SavedChangesAsync:   foreach pending →    │
│                         IAuditWriter.Write   │
└──────────────────────────────────────────────┘
           │
           ▼
┌──────────────────────────┐
│  IAuditWriter            │   (Kun.Audit abstraction)
└──────────────────────────┘
   │                    │                    │
   │ EfAuditWriter      │ EventBusAuditWriter│ custom
   ▼                    ▼                    ▼
 DbSet<AuditLog>    Outbox → RabbitMQ     …
```

Actor & trace attribution is resolved from the existing Kun.Core
primitives, so no new infrastructure is added:

- `ITenantContext.TenantId` → `AuditLog.TenantId`
- `ICurrentUser.UserId` → `AuditLog.ActorId`
- `ITracedCurrentUser.TraceId` / `Activity.Current.TraceId` → `AuditLog.TraceId`
- `ISystemClock.UtcNow` → `AuditLog.Timestamp`

---

## Schema & Query Indexes

The EF configuration creates four B-tree indexes to cover the known
query patterns:

| Index | Pattern |
|-------|---------|
| `(tenant_id, timestamp DESC)` | "show me this tenant's recent activity" |
| `(tenant_id, actor_id, timestamp DESC)` | per-user drill-down |
| `(tenant_id, action, timestamp DESC)` | compliance queries (all `user.login.*`) |
| `(tenant_id, resource_type, resource_id)` | resource history |

The partitioning strategy (monthly by `timestamp`) is deferred to the
consumer's migration — PostgreSQL declarative partitioning is not
expressible in `IEntityTypeConfiguration<T>` and must be emitted via
raw SQL in a migration. Consumers that expect >10M audit rows / month
should add declarative monthly partitioning; the immutability +
append-only index design is already partition-friendly.

---

## Security / Compliance Constraints (per handoff §Security)

| Constraint | How it's addressed in this PR |
|------------|-------------------------------|
| **Tenant isolation** | `AuditLog` implements `IMultiTenant`; when stored in a `KunDbContext`-derived context, the framework-wide automatic tenant global query filter applies. |
| **Append-only** | The interceptor only ever *adds* `AuditLog` rows; it never modifies / deletes. Consumers should revoke `UPDATE` / `DELETE` on `audit_logs` at the DB role level — the framework cannot enforce that from code. |
| **PII handling** | `KunAuditOptions.CaptureEntityPayload = false` disables payload-snapshot capture globally. Per-entity redaction is a **future extension** — consumers that need it should implement a custom `IAuditWriter` decorator. |
| **Retention** | Kun does not own retention; consumers schedule a purge job against `audit_logs WHERE timestamp < now() - interval 'X days'` using an admin role that has `DELETE` privileges the application role does not. |
| **Tamper-evidence (hash chain)** | **Deferred.** Requires either a trigger or a dedicated writer that reads the prior row's `content_hash`. Captured as follow-up, tracked separately. |

---

## AOT Considerations

- `Kun.Audit` passes `dotnet publish -p:PublishAot=true /warnaserror` —
  it has been added to the AOT publish matrix in
  `.github/workflows/ci.yml`.
- `Kun.Audit.EntityFrameworkCore` is **trim-safe only**. Adding it to
  the CI AOT matrix would regress the build because EF Core 10 itself
  is not AOT-strict.
- The interceptor builds its JSON payload via `Utf8JsonWriter` (no
  runtime reflection) so the type can be hoisted to an AOT-strict
  module in a future release if EF Core's AOT support matures. No
  code is reached that calls `JsonSerializer.Serialize(object)` or
  `Activator.CreateInstance`.

---

## Consequences

### Positive

- Clear layering — consumers pick abstraction vs. infrastructure as
  they do for `Kun.EventBus` / `Kun.EventBus.RabbitMQ`.
- Full-AOT consumers retain an audit option (EventBusAuditWriter).
- Unblocks P1-CS-019 / 020 / 021 / 022 without forking audit logic in
  `neuroagent-csharp`.

### Negative / Trade-offs

- Two new csproj entries instead of one — marginal cost, but real.
- Consumers wiring up audit must call three methods:
  `AddKunAudit().AddKunAuditEntityFrameworkCore().UseEntityFramework<T>()`.
  A single convenience extension (`AddFullAuditWithEf<T>()`) could be
  added later if this ergonomic proves painful.

### Risks

| Risk | Mitigation |
|------|------------|
| Interceptor emits noisy audits for trivial writes (e.g. auto-updated `UpdatedAt` timestamps) | The interceptor already filters to aggregate roots; consumers can further scope by registering a custom `IAuditWriter` decorator that drops on `PayloadJson` content. |
| `SwallowWriterExceptions=true` (default) masks writer outages | Logged at Error level with correlation data; compliance-critical deployments should set `SwallowWriterExceptions=false` and accept that audit-infra outages block business writes. |
| No hash-chain today → tampering of DB rows not detectable | Documented; follow-up captured below. Immutability at the DB role layer is still the primary defense. |

---

## Follow-ups

1. **Hash-chain tamper evidence** — write a `HashChainAuditWriter` decorator
   that maintains a `prev_hash` + `content_hash` column pair; add matching
   column + migration guidance to the EF configuration.
2. **Mediator Pipeline Behavior** for `AuditableAttribute` — capture
   Command-level audits (login, export, permission grant) that don't
   trigger any change-tracker entry. Live in `Kun.Audit` (abstraction
   side) since pipeline behaviors are Mediator-layer concerns.
3. **Compliance query DTOs** in `Kun.Audit` — add paginated DTOs that
   consumers can project via `IAuditReader` for P1-CS-020 (compliance
   report API).
4. **Per-entity PII redaction** via an optional `IAuditPayloadRedactor`
   injected into the interceptor.
5. **Declarative monthly partitioning** recipe in `docs/guides/` once
   a consumer hits the 10M-rows-per-month threshold.

---

## References

- Handoff brief: `/home/dawn/project/neuroagent/.omc/autopilot/P1-KN-Audit-handoff.md`
- Gap audit: `docs/neuroagent-phase1-gap.md` §2, §3.2, §4 "需新建"
- Layering rule: `CLAUDE.md` §Architectural constraints (Four-layer dependency flow)
- AOT rule: `CLAUDE.md` §Architectural constraints (AOT compatibility)
- Related ADR: `ADR-001-Kun.Identity-module-decision.md` — the counterpart
  decision *not* to create Identity module, based on the same audit
  principles.
