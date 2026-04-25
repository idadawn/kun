<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-24 | Updated: 2026-04-24 -->

# Kun.Testing.Tests

## Purpose

Unit tests for the fluent assertion helpers in `Kun.Testing`. Covers
every `ResultAssertions` / `DomainEventAssertions` helper on both
the match (pass-through) path and the mismatch (exception-raising)
path — the mismatch-path tests anchor ADR-012's error-message
contract ("helper name + expected + actual always present in the
`KunAssertionException.Message`").

## Key Files

| File | Description |
|------|-------------|
| `Kun.Testing.Tests.csproj` | xUnit v3 test project; references `Kun.Testing` only (transitively pulls `Kun.Core`) |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `Results/` | `ResultAssertionsTests` — success / failure / error-code / error-type |
| `Domain/`  | `DomainEventAssertionsTests` — `ShouldRaise` / `ShouldRaiseExactly` / `ShouldNotRaise` via a local `TestAggregate` fixture |

## For AI Agents

### Working In This Directory

- **Pure in-memory tests.** No DI, no Testcontainers, no HTTP. Treat
  anything that can't run on a laptop offline as out of scope —
  integration coverage for downstream services lives in each
  module's own `Kun.*.Tests` project.
- Every new helper in `src/Kun.Testing` **must** get at least two
  tests here: one pass-through and one mismatch asserting the
  exception message contains both the expected and actual value (use
  FluentAssertions `WithMessage("*expected*actual*")`).
- Test classes use the `{TypeName}Tests` convention; method names
  follow `MethodUnderTest_Scenario_ExpectedResult`.

### Testing Requirements

```bash
dotnet test tests/Kun.Testing.Tests -c Release
```

### Common Patterns

- Asserting the exception-message shape without binding to exact
  punctuation — use wildcards:
  ```csharp
  act.Should().Throw<KunAssertionException>()
      .WithMessage("*ShouldRaise<OrderPlacedEvent>*expected exactly 1*saw 0*");
  ```
- For domain-event helpers, build the test aggregate via a private
  `TestAggregate : AggregateRoot<Guid, Guid>` that exposes
  `AddDomainEvent` through a `Raise(IDomainEvent)` passthrough —
  keeps aggregate construction boilerplate out of every test.

## Dependencies

### Internal

- `Kun.Testing`

### External

- `xunit.v3`, `xunit.runner.visualstudio`
- `Microsoft.NET.Test.Sdk`
- `FluentAssertions` (for `WithMessage` pattern-matching only — not
  for the assertions themselves)
- `coverlet.collector`

<!-- MANUAL: -->
