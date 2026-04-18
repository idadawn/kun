<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Dapper

## Purpose

Read-side data access via Dapper AOT. Query handlers use `[DapperAot]`-annotated methods so the Dapper source generator produces zero-reflection `DbDataReader → DTO` mappers at compile time. This module provides the read-side connection factory abstraction, base classes/extensions for AOT-friendly query methods, and integration helpers for reading from replicas.

## Key Files

| File | Description |
|------|-------------|
| `Kun.Dapper.csproj` | `IsAotCandidate=true` |

## For AI Agents

### Working In This Directory

- **AOT strict**: every query method that returns mapped objects **must** carry `[DapperAot]` so the source generator kicks in. Plain `connection.Query<T>(...)` is forbidden because it falls back to runtime reflection and breaks AOT publish.
- Read-side connections should point at read replicas when possible. Never share a write-side `DbContext` connection with query services — keeping them separate is part of the CQRS discipline here.
- SQL lives inline in query services (use C# raw string literals). Do not construct SQL dynamically with user input — use Dapper's named parameters.
- This module is for **Query handlers only**. Command handlers must use EF Core via `IRepository<T>`.

### Testing Requirements

Query services are tested in `tests/Kun.IntegrationTests` against a Testcontainers PostgreSQL. Given the AOT requirement, add at least one test that exercises each query service path so the generator output is linked and verified.

### Common Patterns

- `IReadDbConnectionFactory.CreateAsync(ct)` returns a disposable `DbConnection` bound to the read-side pool.
- Tenant predicates (`WHERE tenant_id = @TenantId`) are explicit in SQL — there is no global query filter on the read side; the query author is responsible.

## Dependencies

### Internal

- `Kun.Core` — `ITenantContext`.
- `Kun.Infrastructure` — connection factory contract (if abstracted here).

### External

- `Dapper.AOT` — the source generator is the whole point of choosing Dapper here.
- `Npgsql` (default), `MySqlConnector` (alt).

<!-- MANUAL: -->
