# ADR-012: Introduce `Kun.Testing` module

**Status**: Accepted
**Date**: 2026-04-24
**Author**: claude-kun (Kun framework specialist)
**Extends**: none (first ADR dedicated to the testing tooling layer)
**Supersedes**: none

**Context**: Every `tests/Kun.*.Tests` project open-codes the same
shapes:

```csharp
Assert.True(result.IsSuccess);
Assert.Equal("order.notFound", result.Error.Code);
Assert.Contains(order.DomainEvents, e => e is OrderPlacedEvent);
```

FluentAssertions is present, but `Result<T>` / `IAggregateRoot` are
not first-class shapes to it. So the same 3-line block gets retyped
in every test file. `neuroagent-csharp` tests show the same pattern.
ADR-010 warned about consumer drift at the session layer and ADR-011
fixed it at the `Result` composition layer — this ADR closes the
third obvious drift surface at the **assertion** layer.

---

## Decision

Add a new top-level module `Kun.Testing` that exposes fluent
assertion helpers for `Result` / `Result<T>` and `IAggregateRoot`
domain-event collections. The module sits at the **Abstraction
layer** in the four-layer flow (depends on `Kun.Core` only, referenced
downward by consumer test projects).

### Module placement

```
Host         : (no Kun.Testing here — tests consume, don't host)
Infrastructure: (no Kun.Testing here — test fixtures live in per-module Tests projects)
Abstraction  : Kun.Testing      ← NEW
Core         : Kun.Core
```

`Kun.Testing` is **AOT-candidate** (`IsAotCandidate=true`), **packable**
(`IsPackable=true`) so downstream services can consume it as a NuGet,
and carries no runtime `<PackageReference>` beyond `Kun.Core`. The
zero-third-party-dep discipline that guards `Kun.Core` extends one
hop up: testing helpers must not drag FluentAssertions, xUnit, or
any test-framework-specific API into consumer test projects' closure
against their will.

### Framework-agnostic failure signalling

On assertion mismatch, helpers throw a dedicated
`KunAssertionException : Exception` (in namespace
`Kun.Testing`). Consumer test projects using xUnit, NUnit, or MSTest
all surface the throw as a test failure — the message is the
contract, not the exception type.

**Rejected alternatives**:

- *Throw `Xunit.Sdk.XunitException`*: couples `Kun.Testing` to xUnit,
  forcing NUnit/MSTest users to transitively depend on xunit.v3.
- *Throw `FluentAssertions.Execution.AssertionFailedException`*: same
  coupling problem, plus FluentAssertions 8.x switched to a paid
  license which the consumer matrix does not uniformly accept.
- *Return `bool`*: defeats the whole point — tests degrade to
  `Assert.True(result.ShouldBeSuccess())`.

### Surface

All helpers are public static extension methods. Namespaces:

- `Kun.Testing` — `KunAssertionException`
- `Kun.Testing.Results` — `ResultAssertions`
- `Kun.Testing.Domain` — `DomainEventAssertions`

**On `Kun.Core.Results.Result` / `Result<T>`**:

| Helper | Return | Throws if |
|---|---|---|
| `ShouldBeSuccess()` | `void` on `Result`, `T` on `Result<T>` | input is a failure |
| `ShouldBeFailure()` | `Error` | input is a success |
| `ShouldHaveErrorCode(string code)` | `Error` | input is success, or `Error.Code` ≠ `code` |
| `ShouldHaveErrorType(ErrorType type)` | `Error` | input is success, or `Error.Type` ≠ `type` |

The `T`-returning `ShouldBeSuccess` on `Result<T>` lets callers chain
further assertions on the value: `var user = result.ShouldBeSuccess();
user.Email.Should().Be("…");`.

**On `Kun.Core.Domain.IAggregateRoot`**:

| Helper | Return | Throws if |
|---|---|---|
| `ShouldRaise<TEvent>()` | `TEvent` | not exactly one `TEvent` in `DomainEvents` |
| `ShouldRaiseExactly<TEvent>(int count)` | `IReadOnlyList<TEvent>` | `DomainEvents.OfType<TEvent>().Count()` ≠ `count` |
| `ShouldNotRaise<TEvent>()` | `void` | any `TEvent` present |

Using `OfType<TEvent>()` + LINQ is compile-time safe and AOT-friendly
— no reflection, no `MakeGenericMethod`. The type-parameter-erasure
that AOT forbids lives in `Type.GetMethod`, not in generic extension
methods.

### Error-message contract

Every `KunAssertionException` message must contain:

1. The helper name (`ShouldBeSuccess`, `ShouldRaise<T>`, …).
2. What was **expected** (the `T`, `string code`, `int count`).
3. What was **actually seen** (the actual `Error.Code`, the actual
   event-type list, …).

Example:
```
ShouldRaise<OrderPlacedEvent> — expected exactly 1 but saw 0 event(s) of that type. Actual events: [OrderDraftedEvent, OrderLineAddedEvent].
```

Downstream test output — regardless of framework — then carries the
information a reviewer needs without spelunking into source.

### What is **not** in scope

- **Outbox assertions** (`outbox.ShouldContain("order.placed.v1")`).
  `OutboxMessage` lives in `Kun.EntityFrameworkCore`, which is
  explicitly non-AOT (EF Core forces it). Adding a reference from
  `Kun.Testing` to `Kun.EntityFrameworkCore` would drag EF Core into
  every consumer of the testing helpers. Tracked as a future
  `Kun.Testing.EntityFrameworkCore` companion module — ships only if
  a consumer shows concrete demand.
- **Testcontainers / DB fixtures**: per-module Tests projects bring
  their own. `Kun.Testing` is AOT-candidate and pure; fixtures pull
  Docker.
- **ASP.NET Core test server wrappers** (`WebApplicationFactory<T>`):
  belongs in a different module if needed. Non-AOT by construction.
- **Integration-event assertions** on `IntegrationEvent`: shares the
  Outbox-scope logic; deferred for the same reason.

### Adoption strategy

- Smoke-adopt in `tests/Kun.Core.Tests` only during the KN-SELF-002
  implementation — 3–5 call sites, not a mass convert, to prove the
  dependency shape works.
- Other `tests/Kun.*.Tests` and `neuroagent-csharp` tests migrate
  lazily — no repo-wide sweep planned. The ADR sets the default;
  callers converge naturally when they touch each file.
- No version cadence promise: `Kun.Testing` rides the same `-beta` /
  stable release cycle as the rest of the framework.

---

## Alternatives considered

### Put helpers inside `Kun.Core`

Rejected. `Kun.Core` is the framework's runtime seed; adding
assertion helpers there would (a) bloat the NuGet package shipped to
production services and (b) mix runtime and test-only surfaces,
which obscures reviews ("is this used in prod?"). Separation is
cheap when the types live at the same abstraction level.

### Put helpers inside each `Kun.*.Tests` project

Rejected — this is exactly the status quo we're replacing. Each
`Tests` project reinvents the wheel.

### Build on top of FluentAssertions by shipping custom
`Should()` extensions

Rejected. FluentAssertions 8.x is a paid library; propagating a
forced dependency to consumer test projects is not acceptable. Our
own surface is small (≤12 helpers) and doesn't need the extension
machinery.

### Source generator that emits assertion extensions

Interesting but large scope for the value it unlocks. Parked. The
hand-written surface is tiny; generators would win only if the
surface balloons to dozens of helpers.

### Use `System.Diagnostics.Contracts.Contract.Assert`

`Contract.Assert` throws `ContractException` which is internal-ish
and not visible on the public API surface. Bad UX for test output.

---

## Security / observability considerations

- Helpers never log or emit telemetry — they throw synchronously.
- No claim data or tenant context flows through. Safe in any caller.
- The `KunAssertionException.Message` string is the only side-effect
  visible to test runners; helpers pass-through the original
  `Error` object on the returning variants without copying or
  mutating.

## AOT safety

- Zero reflection. Generic extension methods with `T` constraints
  resolve at compile time.
- `OfType<T>()` materialises via sequence filtering — no IL3050.
- No `Expression` trees, no `MakeGenericMethod`, no `Activator`.
- `Kun.Testing.csproj` sets `<IsAotCandidate>true</IsAotCandidate>`;
  Directory.Build.props then wires `IsAotCompatible`,
  `IsTrimmable`, and the trim/AOT/single-file analyzers.

## Impact on existing code

Purely additive. No existing test file changes except the 3–5 smoke-
adoption sites in `tests/Kun.Core.Tests` (which should remain
trivially reversible — each site still compiles with the old
`Assert.*` form if `Kun.Testing` is removed).

## Follow-ups

- **F1**: Companion `Kun.Testing.EntityFrameworkCore` module for
  Outbox / `KunDbContext` assertions if demand surfaces.
- **F2**: Mass-adopt sweep across `tests/Kun.*.Tests` projects after
  KN-SELF-002 ships — possibly delegated to a claude-kun follow-up
  self-managed task (KN-SELF-xxx).
- **F3**: `neuroagent-csharp` adoption tracked separately as a P1
  task if tests drift persists.
