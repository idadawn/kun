<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# tests

## Purpose

Test projects for Kun Framework. Tests are stratified by scope — unit (pure in-memory) for domain and dispatcher logic; integration (Testcontainers PostgreSQL / RabbitMQ / Redis) for cross-layer behavior like Outbox atomicity and consumer idempotency; saga-specific tests for compensation paths.

## Subdirectories

| Directory | Scope | Purpose |
|-----------|------|---------|
| `Kun.Core.Tests/` | Unit | Domain base types, `Result`, `ValueObject` equality (see `Kun.Core.Tests/AGENTS.md`) |
| `Kun.Mediator.Tests/` | Unit | Dispatcher registration, pipeline behavior ordering (see `Kun.Mediator.Tests/AGENTS.md`) |
| `Kun.Saga.Tests/` | Unit + Integration | Saga orchestration, compensation reverse-order, timeout detection (see `Kun.Saga.Tests/AGENTS.md`) |
| `Kun.IntegrationTests/` | Integration (Testcontainers) | Outbox atomicity, tenant isolation, idempotent consumption, gRPC contract (see `Kun.IntegrationTests/AGENTS.md`) |

## For AI Agents

### Working In This Directory

- **Choose the right layer**: domain behaviors ⇒ unit test, anything touching EF Core / RabbitMQ / Redis ⇒ integration test with Testcontainers. Do **not** mock `DbContext` — the framework's correctness around Outbox + transactions depends on the real provider's behavior.
- `xunit.v3`, `FluentAssertions`, `NSubstitute` are the standard stack.
- Integration tests require Docker at the test runner.
- Follow the `kun-testing` skill for naming conventions and arrange/act/assert structure.

### Testing Requirements

```bash
dotnet test                                              # all tests
dotnet test tests/Kun.Mediator.Tests                     # single project
dotnet test --filter "FullyQualifiedName~Outbox"         # subset
dotnet test --collect:"XPlat Code Coverage"              # CI settings
```

### Common Patterns

- Integration fixtures implement `IAsyncLifetime` to spin up containers once per test class.
- Sample fixtures in the `kun-testing` skill show the canonical `KunTestFixture` structure (PostgreSQL + RabbitMQ + Redis + `DbContext` + wait-for-outbox helper).

## Dependencies

### Internal

Test projects reference the `src/Kun.*` module they exercise. Integration tests typically reference several.

### External

- `xunit.v3` + `xunit.runner.visualstudio`
- `FluentAssertions`, `NSubstitute`
- `Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `Testcontainers.Redis`
- `Microsoft.AspNetCore.Mvc.Testing` (gRPC contract tests)

<!-- MANUAL: -->
