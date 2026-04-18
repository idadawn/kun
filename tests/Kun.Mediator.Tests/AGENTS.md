<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Mediator.Tests

## Purpose

Unit tests for `Kun.Mediator` — dispatcher lookup, handler invocation, pipeline behavior composition and ordering, cancellation propagation. No containers, no real DI container beyond `ServiceCollection.BuildServiceProvider()` for wiring.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Mediator.Tests.csproj` | xUnit v3 test project; references `Kun.Mediator` |

## For AI Agents

### Working In This Directory

- **Exercise the compile-time-generated registration**: the source generator should produce an `AddKunMediator(...)` extension; tests should call it and verify handlers are resolvable. Do not manually populate the `FrozenDictionary`.
- **Pipeline ordering is a behavior**, not an implementation detail — write tests that assert behaviors run in the expected order (use a spy behavior that records invocation sequence into a shared list).
- **Cancellation tests**: assert that a `CancellationToken` passed to `Send` reaches the handler and any pipeline behavior.
- **AOT note**: the tests themselves don't need to be AOT-published, but they should not rely on patterns (reflection-based handler discovery) that would mask AOT breakage in the module.

### Testing Requirements

```bash
dotnet test tests/Kun.Mediator.Tests
```

### Common Patterns

- Spy `IPipelineBehavior<,>` implementations that append their own name to a `List<string>` so ordering is assertable as a sequence.
- `NSubstitute` for edge-case substitutes where a real handler would over-complicate the test.

## Dependencies

### Internal

- `Kun.Mediator`, `Kun.Core`

### External

- `xunit.v3`, `FluentAssertions`, `NSubstitute`

<!-- MANUAL: -->
