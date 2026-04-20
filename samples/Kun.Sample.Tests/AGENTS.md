<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-20 | Updated: 2026-04-20 -->

# Kun.Sample.Tests

## Purpose

Integration and end-to-end tests for the Kun Framework sample services. Uses `WebApplicationFactory<T>` with Testcontainers PostgreSQL to exercise the Catalog API and Order Saga orchestration in-process without external infrastructure. Demonstrates cross-service testing patterns including `extern alias` for multi-Program projects and fake gRPC clients for deterministic saga tests.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Sample.Tests.csproj` | References Catalog and Order sample projects with `Aliases` for `extern alias` disambiguation; xunit.v3 + FluentAssertions + Testcontainers.PostgreSql |
| `CatalogApiTests.cs` | E2E Catalog API tests: POST/GET product, validation failure (400) |
| `OrderSagaTests.cs` | E2E saga tests: happy path, payment failure compensation, timeout compensation, inventory reservation failure |
| `CrossServiceTracingTests.cs` | Distributed trace propagation across service boundaries |
| `Fixtures/PostgresFixture.cs` | Shared Testcontainers PostgreSQL container fixture |

## For AI Agents

### Working In This Directory
- Tests run against **in-memory WebApplicationFactory** instances — no Docker Desktop required beyond Testcontainers' PostgreSQL container.
- The `OrderSagaTests` use a **fake `CatalogServiceClient`** (returns in-memory responses via `NullCallInvoker`) to avoid cross-service networking. Do not replace with live gRPC calls without explicit justification.
- `extern alias CatalogApp` and `extern alias OrderApp` are required because both sample services define a top-level `Program` class in the global namespace.

### Testing Requirements
```bash
dotnet test samples/Kun.Sample.Tests -c Release
```
All tests should pass in under 2 minutes (saga timeout test has a 120s xunit timeout).

### Common Patterns
- `IAsyncLifetime` fixtures for Testcontainers lifecycle.
- `ConfigureTestServices` to replace gRPC clients with fakes.
- Poll-based state assertions for async saga completion.

## Dependencies

### Internal
- `Kun.Sample.Catalog` — Catalog sample service under test.
- `Kun.Sample.Order` — Order sample service under test.

### External
- xunit.v3, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.PostgreSql.

<!-- MANUAL: -->
