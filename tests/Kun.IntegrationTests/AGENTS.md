<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.IntegrationTests

## Purpose

Integration tests that require real infrastructure. Runs against Testcontainers PostgreSQL, RabbitMQ, Redis — never mocks. Covers Outbox atomicity (aggregate + outbox commit together), tenant query filter enforcement, idempotent consumer behavior under message redelivery, Saga end-to-end flows including background services, and gRPC contract tests.

## Key Files

| File | Description |
|------|-------------|
| `Kun.IntegrationTests.csproj` | xUnit v3 test project; references multiple `Kun.*` modules |

## For AI Agents

### Working In This Directory

- **Do not mock `DbContext`, `IConnection` (RabbitMQ), or `ConnectionMultiplexer` (Redis).** The whole point of this project is catching divergence between unit-level mocks and real provider behavior — mocks here would defeat that.
- **Use a shared `KunTestFixture`** (class-fixture pattern) that starts all three containers once per test class. Pattern comes from the `kun-testing` skill — follow it.
- **Outbox test pattern**:
  1. Start a transaction / use a scoped `DbContext`.
  2. Add a domain event to an aggregate, call `uow.SaveChangesAsync()`.
  3. `await fixture.WaitForOutboxDrainAsync()` (polls `OutboxMessage.Status == Published`).
  4. Assert the expected integration event was consumed.
- **Tenant-isolation test pattern**: create data for tenant A, set `ITenantContext` to tenant B, assert query returns nothing, then flip back and assert visibility.
- **Docker must be running** on the test runner. CI declares service containers in `.github/workflows/ci.yml`; locally you need Docker Desktop / equivalent.

### Testing Requirements

```bash
dotnet test tests/Kun.IntegrationTests
```

Tests are slow relative to unit projects — expect minutes, not seconds. Run them selectively during iteration and let CI run the full set.

### Common Patterns

- `IAsyncLifetime.InitializeAsync` spins containers; `DisposeAsync` tears them down.
- Test names describe the behavior being verified: `SaveChanges_WithDomainEvent_ShouldPublishIntegrationEvent`.
- Cleanup between tests: rather than tearing down containers, truncate tables / purge queues — much faster.

## Dependencies

### Internal

- `Kun.EntityFrameworkCore`, `Kun.EventBus.RabbitMQ`, `Kun.Saga`, `Kun.AspNetCore`, `Kun.Grpc`, `Kun.Security`

### External

- `xunit.v3`, `FluentAssertions`
- `Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `Testcontainers.Redis`
- `Microsoft.AspNetCore.Mvc.Testing`

<!-- MANUAL: -->
