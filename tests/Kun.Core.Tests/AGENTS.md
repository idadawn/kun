<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Core.Tests

## Purpose

Unit tests for `Kun.Core` — domain primitives, `Result<T>` semantics, `ValueObject` equality, `Entity<TKey>` equality rules, `Specification<T>` composition (if placed here). Pure in-memory; no containers.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Core.Tests.csproj` | xUnit v3 test project; `IsPackable=false`, references `Kun.Core` |

## For AI Agents

### Working In This Directory

- **Purely synchronous, in-memory tests.** Do not introduce Testcontainers, DI container setup, or EF Core in this project — cross-layer behavior belongs in `Kun.IntegrationTests`.
- Focus coverage on domain invariants (e.g. aggregate method refuses invalid state), `Result` construction rules (success cannot carry an error, failure must carry one), `ValueObject.Equals` across equal/unequal component sequences, transient-entity inequality (two entities with default `Id` are not equal).
- Follow arrange/act/assert with `FluentAssertions` — e.g. `order.Status.Should().Be(OrderStatus.Draft)`.

### Testing Requirements

```bash
dotnet test tests/Kun.Core.Tests
dotnet test tests/Kun.Core.Tests --filter "FullyQualifiedName~ValueObject"
```

### Common Patterns

- One test class per type under test; file named `{TypeName}Tests.cs`.
- Method naming: `MethodUnderTest_Scenario_ExpectedResult`.

## Dependencies

### Internal

- `Kun.Core`

### External

- `xunit.v3`, `xunit.runner.visualstudio`
- `FluentAssertions`
- `coverlet.collector`

<!-- MANUAL: -->
