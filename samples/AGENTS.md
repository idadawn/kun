<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# samples

## Purpose

Runnable example projects demonstrating Kun Framework end-to-end: DDD aggregates, CQRS handlers, Outbox events, Saga orchestration, gRPC between services, S2S JWT, multi-tenant queries. Orchestrated with .NET Aspire so `dotnet run` on the AppHost brings up PostgreSQL, RabbitMQ, Redis, and all services at once.

## Key Files

| File | Description |
|------|-------------|
| `README.md` | List of planned samples and instructions for running them |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `Kun.Sample.Catalog/` | Planned: product catalog service (DDD + CQRS + Outbox + gRPC). Currently an empty placeholder. |

## For AI Agents

### Working In This Directory

- **Status**: no sample code exists yet — the directory is a placeholder. Building out a sample from scratch is a substantial task; confirm scope with the user before starting.
- Samples must **use the framework as a consumer**, not depend on `src/Kun.*` projects via `ProjectReference` unless the sample lives inside the same solution for easier debugging. If using `ProjectReference`, the Aspire AppHost still brings them up as if they were NuGet consumers.
- Samples are **not published as packages** — do not set `<IsPackable>true</IsPackable>` on sample csproj files.
- Use the `kun-*` skills to ensure sample code reflects canonical framework usage, not ad-hoc patterns.

### Testing Requirements

Samples aren't part of `dotnet test`. Validate by running the AppHost and hitting endpoints manually, or by adding integration tests that spin up the sample service in-process.

### Common Patterns

Planned layout per sample service:

```
Kun.Sample.Catalog/
  Kun.Sample.Catalog.Domain/           # Aggregates, value objects, domain events
  Kun.Sample.Catalog.Application/      # Commands, queries, handlers
  Kun.Sample.Catalog.Infrastructure/   # EF mappings, read services
  Kun.Sample.Catalog.Api/              # Minimal API + gRPC host
  Kun.Sample.Catalog.AppHost/          # Aspire orchestration
```

## Dependencies

### External

- `Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.RabbitMQ`, `Aspire.Hosting.Redis`

<!-- MANUAL: -->
