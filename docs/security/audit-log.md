# Operation Audit Log: Hash-Chained Command Trail

Kun records one `AuditEntry` per command handler invocation. Entries are
chained by SHA-256 so retroactive tampering with any row invalidates every
row after it. This is the same property that `audit.operations.prev_hash`
+ `content_hash` gives SAD v3.0 §15.5 — Kun.Audit is the framework-level
primitive that NeuroAgent-style services plug into.

This document explains what gets recorded, how the hash chain works, what
guarantees it offers (and what it does not), and how to wire it up.

---

## Shape of a Record

```csharp
public sealed record AuditEntry(
    Guid Id,
    string? TenantId,
    string? ActorId,
    string CommandName,
    DateTimeOffset OccurredAt,
    long LatencyMs,
    AuditOutcome Outcome,          // Success | Exception
    string? ErrorCode,             // exception type name on failure
    string? PrevHash,              // null for the first entry in the tenant's chain
    string ContentHash);           // sha256 over prev + canonical fields
```

Each entry is attached to a single tenant (or to the global chain when
`ITenantContext.TenantId` is null). `ContentHash` is lower-case hex so it
is safe to drop into DB columns, URLs, Markdown tables, or trace tags
without re-encoding.

---

## How the Chain Hashes

```
canonical  = $"{Id:N}|{TenantId}|{ActorId}|{CommandName}|{OccurredAt:O}|{LatencyMs}|{Outcome}|{ErrorCode}"
hash       = sha256(UTF-8(PrevHash ?? "") || UTF-8(canonical))
content    = lowercase_hex(hash)
```

Two properties matter:

1. **Determinism.** Two different callers hashing the *same* logical entry
   produce the same output. `CultureInfo.InvariantCulture`, the `O`
   round-trip timestamp format, and invariant enum names keep the hash
   stable across cultures, time zones, and runtimes.
2. **Tamper evidence.** Flipping any field — including `PrevHash` — changes
   `ContentHash`. So editing row *N* in place invalidates rows *N+1*, *N+2*,
   … An auditor can replay the chain from any known-good anchor and detect
   mutations with no extra metadata.

---

## Serialisation Contract (per Tenant)

`IAuditChainState.AppendAsync` is the single atomic "read previous,
compute new, commit new" operation. Implementations serialise it per
tenant key so two concurrent commands on the same tenant cannot fork the
chain. The signature forces callers into the serialised path:

```csharp
await chainState.AppendAsync(
    tenantKey,
    async prev =>
    {
        var entry = BuildEntry(prev);
        await sink.WriteAsync(entry, ct);
        return entry.ContentHash;         // becomes the tenant's new tip
    },
    ct);
```

Implementations are free to centralise the lock and the sink write inside
the `AppendAsync` transaction — which is what a durable DB implementation
should do so the prev-lookup and the row insert happen under the same
serialisable transaction.

---

## Included Implementations and Their Limits

### `InMemoryAuditChainState` + `InMemoryAuditSink`

Process-local. Keeps the chain tip in a `ConcurrentDictionary` and the
entries in a `ConcurrentQueue`. Each tenant gets its own `SemaphoreSlim`.

**Good for**: unit tests, single-instance dev runs, sample apps.

**Not good for**:

- Multi-instance deployments — two replicas do not share a tip, so they
  will both append to a tenant chain with `PrevHash = null` and fork it.
  The fork is discoverable (two rows sharing the same prev), but you
  should not rely on in-memory state if that matters.
- Durability — a process crash loses every unpersisted tip *and* every
  un-flushed `InMemoryAuditSink` entry.

### Expected production shape

Replace both defaults with one durable implementation that:

1. Reads the current chain tip in a serialisable transaction.
2. Inserts the row with `content_hash` as a UNIQUE constraint, so
   replicas trying to append under the same prev either succeed once or
   retry.
3. Updates the chain-tip view in the same transaction.

A template EF Core implementation is out of scope for this doc — keep
your audit table in your service schema (Outbox-adjacent) and register
the EF sink with `services.AddSingleton<IAuditSink, MyEfAuditSink>()`
before (or instead of) `AddKunAudit()`.

---

## Wiring

```csharp
// 1. Register defaults: in-memory chain + in-memory sink + TimeProvider.
services.AddKunAudit();

// 2. Opt each auditable command into the Mediator pipeline.
services.AddKunMediator(b => b
    .AddAuditBehavior<CreateOrderCommand, Guid>()
    .AddAuditBehavior<CancelOrderCommand, Result>()
    .AddRequestHandler<CreateOrderCommand, Guid, CreateOrderHandler>()
    .AddRequestHandler<CancelOrderCommand, Result, CancelOrderHandler>());

// 3. In production, override defaults with durable implementations
//    BEFORE AddKunAudit, so the TryAdd inside it becomes a no-op:
services.AddSingleton<IAuditSink, MyEfAuditSink>();
services.AddSingleton<IAuditChainState, MyDbChainState>();
services.AddKunAudit();
```

Queries and events stay unaudited by default — only call
`AddAuditBehavior<,>()` for the commands that belong in the audit trail.

---

## Failure Semantics

The behaviour preserves three invariants regardless of what the sink or
chain state does:

1. **Handler exceptions reach the caller unchanged.** The original
   exception is captured, surfaced in the row as
   `Outcome = Exception, ErrorCode = <typeName>`, and then rethrown via
   `ExceptionDispatchInfo.Capture(ex).Throw()` so the stack trace is
   preserved.
2. **Successful handlers never fail because of an audit write.** A
   failed `IAuditSink.WriteAsync` or a thrown `AppendAsync` is caught,
   logged at Warning (via `LoggerMessage` source-gen), and swallowed.
3. **Audit failures never mask a business exception.** When both the
   handler *and* the audit write throw, the audit exception is dropped
   (logged) and the handler's exception is the one the caller sees.

---

## What Kun.Audit Does *Not* Cover

- **Row-level auditing** (who last updated an aggregate). That is
  `IAuditedObject<TUserKey>` and the `AggregateRoot` stamps — a
  separate concern in `Kun.Core.Domain.Auditing`.
- **Business-level outcome detail.** Success vs Exception is what the
  MVP records; mapping `Result.Fail(Error.Validation(...))` to a dedicated
  `Failure` outcome with the error code is a follow-up.
- **Request / response bodies.** Arguments and payloads are *not*
  recorded by default. If you need parameter capture, wrap the behaviour
  and write an extended sink — keep an eye on PII.
- **Multi-instance durability.** See the "Expected production shape"
  section.

---

## Verifying a Chain

Given a run of `N` entries returned in order for one tenant, an auditor
recomputes `ContentHash` from scratch:

```csharp
string? running = null;
foreach (var row in rowsInOrder)
{
    var expected = AuditContentHasher.Compute(
        row.Id, row.TenantId, row.ActorId, row.CommandName,
        row.OccurredAt, row.LatencyMs, row.Outcome, row.ErrorCode,
        running);
    if (expected != row.ContentHash)
        throw new AuditChainBrokenException(row.Id);
    running = row.ContentHash;
}
```

If every entry survives this pass, the chain is intact from the anchor
point forward. If a row fails, it and every row after it are suspect
until the next anchor.
