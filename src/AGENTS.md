<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# src

## Purpose

The 14 framework modules that compose Kun Framework. Each module is a distinct NuGet package and a csproj here. Modules are layered Core → Abstraction → Infrastructure → Host; dependencies flow downward only.

## Key Files

| File | Description |
|------|-------------|
| `README.md` | Module matrix with AOT-Ready status and links |

## Subdirectories

| Directory | Layer | AOT | Purpose |
|-----------|:---:|:---:|---------|
| `Kun.Core/` | Core | ✅ | Domain primitives, CQRS contracts, `Result`, `ITenantContext` — zero third-party deps (see `Kun.Core/AGENTS.md`) |
| `Kun.Mediator/` | Abstraction | ✅ | Reflection-free CQRS dispatcher (see `Kun.Mediator/AGENTS.md`) |
| `Kun.Infrastructure/` | Abstraction | ✅ | Repository / UoW / Specification / `IProcessedMessageStore` interfaces (see `Kun.Infrastructure/AGENTS.md`) |
| `Kun.EventBus/` | Abstraction | ✅ | Domain / integration event abstractions, Outbox publisher contract (see `Kun.EventBus/AGENTS.md`) |
| `Kun.Security/` | Abstraction | ✅ | S2S JWT issuance / validation, tenant context wiring (see `Kun.Security/AGENTS.md`) |
| `Kun.Validation/` | Abstraction | ✅ | Source-generated validators (see `Kun.Validation/AGENTS.md`) |
| `Kun.EntityFrameworkCore/` | Infrastructure | ⚠️ trim-safe | Write-side EF Core, Outbox writer, tenant filters (see `Kun.EntityFrameworkCore/AGENTS.md`) |
| `Kun.Dapper/` | Infrastructure | ✅ | Read-side Dapper AOT wrapper (see `Kun.Dapper/AGENTS.md`) |
| `Kun.EventBus.RabbitMQ/` | Infrastructure | ✅ | RabbitMQ transport + idempotent consumer decorator (see `Kun.EventBus.RabbitMQ/AGENTS.md`) |
| `Kun.Saga/` | Infrastructure | ✅ | Saga Orchestration engine (see `Kun.Saga/AGENTS.md`) |
| `Kun.Storage/` | Infrastructure | ⚠️ trim-safe | IFileStorage with S3/MinIO and local backends + Content-Disposition policy (see `Kun.Storage/AGENTS.md`) |
| `Kun.AspNetCore/` | Host | ✅ | Minimal API extensions + Pipeline Behaviors (see `Kun.AspNetCore/AGENTS.md`) |
| `Kun.Grpc/` | Host | ✅ | gRPC interceptors, `Result → RpcException` mapping (see `Kun.Grpc/AGENTS.md`) |
| `Kun.Gateway/` | Host | ✅ | YARP gateway, S2S JWT issuance, rate limiting (see `Kun.Gateway/AGENTS.md`) |

## For AI Agents

### Working In This Directory

- **Layer enforcement is hard**: a module here must not reference any module in a higher layer. CI + review will reject violations.
- **`Kun.Core` cannot take *any* third-party dependency**, not even EF Core or Dapper.
- **New module?** Add csproj, register in `Kun.sln`, declare `<IsAotCandidate>true|false</IsAotCandidate>`, avoid setting `TargetFramework`/`Nullable` (inherited from `Directory.Build.props`), reference packages via central versioning (no `Version=` in csproj).

### Testing Requirements

Each module should have a corresponding `tests/Kun.{Module}.Tests` project. Tests for cross-layer behavior (Outbox, Saga, multi-tenancy) go in `tests/Kun.IntegrationTests` with Testcontainers.

### Common Patterns

- `<IsPackable>true</IsPackable>` — every src module is a NuGet package.
- XML doc comments for all public APIs (`GenerateDocumentationFile=true`). CS1591 warns but does not fail.
- Namespace = assembly name; sub-namespaces reflect responsibility groups (e.g. `Kun.Core.Domain`, `Kun.Core.Results`).

## Dependencies

### Internal

None (this is the source root).

### External

All package versions pinned in `Directory.Packages.props`.

<!-- MANUAL: -->
