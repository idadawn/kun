<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-24 | Updated: 2026-04-24 -->

# Kun.Testing.EntityFrameworkCore.Tests

## Purpose

Test project for the
[Kun.Testing.EntityFrameworkCore](../../src/Kun.Testing.EntityFrameworkCore/)
module — exercises `OutboxAssertions` helpers across both the
`IEnumerable<OutboxMessage>` and `DbContext` overload families.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Testing.EntityFrameworkCore.Tests.csproj` | `IsPackable=false`. xUnit.v3 + FluentAssertions + coverlet. `Microsoft.EntityFrameworkCore.InMemory` for the DbContext overload fixture. |
| `OutboxAssertionsTests.cs` | Scenarios for each helper × each overload. |

## For AI Agents

### Working In This Directory

- **Test-local fixtures only.** Need an `OutboxMessage` with
  specific `EventTypeId`? Build it inline — do not lobby for a
  public factory (that's deferred per ADR-015 §F3-Out-of-scope).
- **DbContext tests use `UseInMemoryDatabase`** with a unique
  name per test (e.g. `Guid.NewGuid().ToString()`) to avoid
  cross-test interference. The InMemory provider tolerates an
  entity without an `IEntityTypeConfiguration<T>` in simple
  scenarios; `OutboxMessage` has a private parameterless
  constructor for EF Core use, so it composes cleanly.
- **FluentAssertions `.WithMessage("*helper*expected*observed*")`**
  anchors on the mismatch-message shape per the module's
  AGENTS §mismatch-message contract without coupling to exact
  punctuation.

### Testing Requirements

- Cover every helper's pass + fail paths (both overload
  families).
- At least one negative-path test per helper anchors on the
  helper name + expected `eventTypeId` + observed count in the
  `KunAssertionException.Message`.

<!-- MANUAL: -->
