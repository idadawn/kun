<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-24 | Updated: 2026-04-24 -->

# Kun.Audit

## Purpose

Tamper-evident operation audit for CQRS commands. Satisfies SAD v3.0 §15.5 (`audit.operations` + hash chain) for NeuroAgent Phase 1 (P1-CS-019). A Mediator pipeline behaviour captures each command's actor / tenant / timing / outcome, pushes it through `IAuditChainState.AppendAsync`, and writes one `AuditEntry` to `IAuditSink`. Each entry's `ContentHash` is SHA-256 over `prevHash || canonicalFields`, so retroactive tampering with any row invalidates every following row.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Audit.csproj` | `IsAotCandidate=true`, Abstraction layer; depends only on `Kun.Core` + `Kun.Mediator` |
| `AuditEntry.cs` | Immutable record — Id, TenantId, ActorId, CommandName, OccurredAt, LatencyMs, Outcome, ErrorCode, PrevHash, ContentHash |
| `AuditOutcome.cs` | Success / Exception (Result.Fail mapping is a follow-up) |
| `IAuditSink.cs` | Pluggable write target (`WriteAsync(AuditEntry)`) |
| `IAuditChainState.cs` | Single atomic `AppendAsync(tenantKey, factory)` that serialises read-previous / compute-new / commit-new |
| `AuditContentHasher.cs` | Canonical `pipe \| separated \| fields` + SHA-256, lower-case hex |
| `InMemoryAuditChainState.cs` | Per-tenant SemaphoreSlim + ConcurrentDictionary; dev / single-instance use only |
| `InMemoryAuditSink.cs` | ConcurrentQueue sink for tests |
| `Behaviors/AuditLogPipelineBehavior.cs` | Mediator `IPipelineBehavior<,>` that captures the entry |
| `DependencyInjection/KunAuditServiceCollectionExtensions.cs` | `AddKunAudit()` + `AddAuditBehavior<TCommand, TResponse>()` |

## For AI Agents

### Working In This Directory

- **AOT-safe**: zero runtime reflection. The behaviour is closed-generic per command type at registration time (mirrors the `Kun.Validation` and `Kun.Mediator` pattern). Do not introduce `Type.MakeGenericType` or attribute-based discovery.
- **`TryAdd` every default in `AddKunAudit`**: in-memory chain state and sink are for tests / single-instance dev only. Production deployments override both with durable implementations — never swap the `TryAdd` for `Add`, or you break that extensibility.
- **Audit failure must never break a command**: `AuditLogPipelineBehavior` wraps the audit write in its own try/catch that logs at Warning. If you add new sinks / chain states, preserve that contract — the handler's outcome is authoritative.
- **Preserve the original exception**: when the handler throws, the behaviour captures it, writes the audit row with `Outcome=Exception` + `ErrorCode=<ExceptionTypeName>`, then rethrows via `ExceptionDispatchInfo.Capture(ex).Throw()` so the original stack trace is preserved.
- **Per-tenant serialisation is the whole point**: `InMemoryAuditChainState.AppendAsync` uses a per-tenant `SemaphoreSlim`. If you add a durable chain state, it must guarantee the same read-prev / compute-new / commit-new is atomic per tenant — typically via DB-level uniqueness on `(tenant_id, content_hash)` or a serialisable transaction.
- **Opt-in per command**: `AddAuditBehavior<TCommand, TResponse>()` registers exactly one closed generic instance. Queries and events stay unaudited by default — add the behaviour only to commands that belong in the audit trail.
- **Canonical hash is invariant across culture and timezone**: `AuditContentHasher` uses `CultureInfo.InvariantCulture`, round-trip `O` timestamp format, and lower-case hex. Do not switch to binary / base-64 without updating every downstream reader.

### Testing Requirements

- Unit coverage lives in `tests/Kun.Audit.Tests` (xunit.v3, FluentAssertions, NSubstitute):
  - Hash matrix — every field mutation shifts the hash.
  - Chain state — 100 concurrent appends on one tenant observe distinct prevs and form a linked list (no fork).
  - Pipeline behaviour — success / exception paths, sink failure does not propagate, consecutive entries link, per-tenant chains stay independent.
- Integration tests with a durable sink are deferred until a durable implementation ships (out of Kun.Audit's scope — consumers wire their own EF sink for now).

## Dependencies

### Internal

- `Kun.Core` — `Kun.Core.Cqrs.IRequest<>`, `Kun.Core.MultiTenancy.ITenantContext`, `Kun.Core.Security.ICurrentUser`.
- `Kun.Mediator` — `IPipelineBehavior<,>`, `KunMediatorBuilder`.

### External

- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

<!-- MANUAL: -->
