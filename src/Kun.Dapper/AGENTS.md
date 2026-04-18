<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun.Dapper

## Purpose

Read-side abstractions for Dapper-based query handlers: connection factory contract, pagination helpers, and DI extensions. Concrete implementations (e.g., `NpgsqlReadDbConnectionFactory`) live in `Kun.Infrastructure`.

## Key Files

| File | Description |
|------|-------------|
| `IReadDbConnectionFactory.cs` | Opens read-only ADO.NET connections |
| `PageRequest.cs` | 1-based pagination parameters with SQL offset helper |
| `PagedResult.cs` | Generic paged result with total-count metadata |
| `ReadDbOptions.cs` | Connection-string configuration DTO |
| `DependencyInjection/KunDapperServiceCollectionExtensions.cs` | AddKunDapper DI entry point |

## For AI Agents

### Working In This Directory
- No Dapper-specific query code lives here — this is pure abstractions + pagination primitives.
- Concrete `IReadDbConnectionFactory` implementations (Npgsql, SqlClient) belong in `Kun.Infrastructure`.
- `PageRequest.Page` is 1-based; `PageRequest.Offset` converts to 0-based for SQL.
- `ReadDbOptions` is registered as Singleton by `AddKunDapper`.

### Testing Requirements
```bash
dotnet test tests/Kun.Dapper.Tests -c Release
```

## Dependencies

### Internal
- `Kun.Core` — domain contracts

### External
- `Dapper` — micro-ORM
- `Microsoft.Extensions.DependencyInjection.Abstractions`

<!-- MANUAL: -->
