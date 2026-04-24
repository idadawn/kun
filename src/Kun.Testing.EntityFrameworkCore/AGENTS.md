<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-24 | Updated: 2026-04-24 -->

# Kun.Testing.EntityFrameworkCore

## Purpose

Sibling to `Kun.Testing` that ships assertion helpers for
EF-persisted `OutboxMessage` state. Consumers of the Outbox
pattern (see
[ADR-015](../../docs/architecture/ADR-015-Outbox-pattern.md))
writing "does my command stage the right `OutboxMessage` in-
transaction?" tests compose these helpers instead of hand-
rolling `dbContext.Set<OutboxMessage>().ToList()` + match-by-
`EventTypeId` loops at every call site. Closes ADR-015 §F3.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Testing.EntityFrameworkCore.csproj` | `IsAotCandidate=false` (mirrors `Kun.EntityFrameworkCore`), `IsPackable=true`, `HasPublicApi=true`. ProjectReferences `Kun.Testing` + `Kun.EventBus`; PackageReference `Microsoft.EntityFrameworkCore`. |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `Outbox/` | `OutboxAssertions` — `ShouldHaveOutboxMessage` / `ShouldHaveExactlyOneOutboxMessage` / `ShouldNotHaveOutboxMessage` on both `DbContext` and `IEnumerable<OutboxMessage>`. |

## For AI Agents

### Working In This Directory

- **Non-AOT is deliberate.** EF Core is reflection-heavy; this
  module follows `Kun.EntityFrameworkCore` with
  `IsAotCandidate=false`. `Kun.Testing` (the sibling) stays
  AOT-strict; do not back-port an EF dependency there.
- **Throw `KunAssertionException`.** Reuse the exception type
  from `Kun.Testing` — keeps framework-agnostic assertions
  consistent across the module pair.
- **Mismatch messages must include**: helper name, expected
  `eventTypeId`, observed count, and (where relevant) the
  distinct event-type IDs actually present. Test-runner output
  is the whole UX — bare "assertion failed" fails the ADR-012
  §Error-message contract this module inherits.
- **Synchronous enumeration.** Consumers have already called
  `SaveChangesAsync`; helpers enumerate `db.Set<OutboxMessage>()`
  synchronously via `AsEnumerable()`. Don't ship an
  `*Async` variant unless a concrete consumer need surfaces.
- **Public API additions** must land in `PublicAPI.Unshipped.txt`
  in the same commit (standard `HasPublicApi=true` rule).

### Testing Requirements

Tests live in `tests/Kun.Testing.EntityFrameworkCore.Tests`.
Every helper has: a pass-through test, a mismatch-exception-
message test (anchor on expected `eventTypeId` + observed
count in the message), and the parallel test for the
`IEnumerable<OutboxMessage>` overload. No real EF `DbContext`
is needed for the collection overload; the DbContext overload
can be covered via a small in-memory test harness or the
collection overload if the convenience wrapper is a one-liner.

### Common Patterns

- Assert one outbox row after a command handler saves:
  ```csharp
  await _mediator.Send(new PlaceOrderCommand(...), ct);
  _dbContext.ShouldHaveExactlyOneOutboxMessage("order.placed.v1");
  ```

- Work on a pre-queried snapshot (e.g. shared across
  multiple assertions):
  ```csharp
  var outbox = _dbContext.Set<OutboxMessage>().ToArray();
  outbox.ShouldHaveOutboxMessage("order.placed.v1");
  outbox.ShouldNotHaveOutboxMessage("payment.refunded.v1");
  ```

## Dependencies

### Internal

- `Kun.Testing` — reuses `KunAssertionException`.
- `Kun.EventBus` — `OutboxMessage` is the type under test.

### External

- `Microsoft.EntityFrameworkCore` — `DbContext` extension
  surface.

<!-- MANUAL: -->
