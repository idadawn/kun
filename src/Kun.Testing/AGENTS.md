<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-24 | Updated: 2026-04-24 -->

# Kun.Testing

## Purpose

Framework-agnostic fluent assertion helpers for `Result` / `Result<T>`
and `IAggregateRoot` domain-event collections, consumed by every
`tests/Kun.*.Tests` project and by downstream services' own test
suites. See [ADR-012](../../docs/architecture/ADR-012-Kun.Testing-module.md)
for the module rationale and boundary.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Testing.csproj` | `IsAotCandidate=true`, `IsPackable=true`, `HasPublicApi=true`, single `ProjectReference` to `Kun.Core` |
| `KunAssertionException.cs` | Sealed `Exception` subclass thrown on assertion mismatch — framework-agnostic (no xUnit / FluentAssertions dep) |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `Results/` | `ResultAssertions` — `ShouldBeSuccess` / `ShouldBeFailure` / `ShouldHaveErrorCode` / `ShouldHaveErrorType` |
| `Domain/`  | `DomainEventAssertions` — `ShouldRaise<T>` / `ShouldRaiseExactly<T>` / `ShouldNotRaise<T>` |

## For AI Agents

### Working In This Directory

- **Zero third-party runtime deps.** `Kun.Testing.csproj` references
  only `Kun.Core`. Do not add `<PackageReference>` (not even xUnit or
  FluentAssertions) — the whole point is framework-agnosticism per
  ADR-012.
- **AOT strict.** No reflection, no `MakeGenericMethod`, no
  `Expression` trees. Use generic type parameters + LINQ
  (`OfType<T>()`) for type-directed assertions.
- **Every mismatch message must include the helper name, the expected
  value, and the actual observed value.** Test-runner output is the
  whole UX; a bare "assertion failed" without context fails the ADR
  contract (§Error-message contract).
- Return the inspected value where it helps chaining
  (`ShouldBeSuccess<T>() -> T`, `ShouldHaveErrorCode(string) -> Error`,
  `ShouldRaise<T>() -> T`). Don't return `void` just to keep the
  signature simple — the returned value removes an extra line in
  consumer tests.
- Public API additions must land in `PublicAPI.Unshipped.txt` in the
  same commit that adds them (standard `HasPublicApi=true` rule).

### Testing Requirements

Tests live in `tests/Kun.Testing.Tests`. Every helper has a
pass-through test, an exception-message-shape test, and any applicable
argument guard (null / out-of-range). Use FluentAssertions
`WithMessage("*expected*actual*")` to anchor the ADR message contract
without coupling to exact punctuation.

### Common Patterns

- Terse consumer tests:
  ```csharp
  // Arrange
  var cmd = new CreateOrderCommand(customer: null!);

  // Act
  var result = await _handler.Handle(cmd, CancellationToken.None);

  // Assert
  result.ShouldHaveErrorCode("order.customer.required");
  ```

- Chain via the returned value:
  ```csharp
  var user = (await _users.GetAsync(id, ct)).ShouldBeSuccess();
  user.Email.Should().Be("a@b.com");
  ```

- Assert exactly one domain event was raised, then inspect it:
  ```csharp
  order.Cancel();
  var evt = order.ShouldRaise<OrderCancelledEvent>();
  evt.Reason.Should().Be(CancelReason.CustomerRequest);
  ```

## Dependencies

### Internal

- `Kun.Core`

### External

None.

<!-- MANUAL: -->
