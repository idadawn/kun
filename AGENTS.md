<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-18 -->

# Kun Framework

## Purpose

A .NET 10 cloud-native microservice framework with an AOT-first lean: strict DDD, dual-engine data access (EF Core write side + Dapper AOT read side), reflection-free CQRS dispatcher, Outbox-based event-driven messaging, in-house Saga orchestrator, zero-trust S2S JWT between services, and first-class multi-tenancy. The repo is at **project-skeleton stage** — scaffolding, csproj files, CI, docs, and skills are in place; most `src/Kun.*` modules are empty beyond a csproj.

## Key Files

| File | Description |
|------|-------------|
| `Kun.sln` | Solution with 13 src projects + 4 test projects |
| `Directory.Build.props` | Global MSBuild config: `net10.0`, nullable on, `TreatWarningsAsErrors`, AOT analyzers gated by `<IsAotCandidate>` |
| `Directory.Packages.props` | Central Package Management — all NuGet versions are pinned here, csproj files do not set `Version=` |
| `global.json` | Pins .NET SDK 10.0.100 |
| `.editorconfig` | Code style enforced by `dotnet format` in CI |
| `CLAUDE.md` | Authoritative instructions for Claude Code working in this repo |
| `CONTRIBUTING.md` | Human-facing contribution rules — same constraints as CLAUDE.md, less dense |
| `README.md` | Project overview, quick-start, module matrix |
| `CHANGELOG.md` | Keep-a-Changelog format, SemVer |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `src/` | 13 framework modules `Kun.*` (see `src/AGENTS.md`) |
| `tests/` | Unit, integration, and saga test projects (see `tests/AGENTS.md`) |
| `samples/` | Sample apps demonstrating framework usage (see `samples/AGENTS.md`) |
| `docs/` | Architecture whitepaper and module map (see `docs/AGENTS.md`) |
| `.claude/` | Kun-specific Claude Skills that auto-load in Claude Code (see `.claude/AGENTS.md`) |
| `.github/` | CI workflow + issue/PR templates |

## For AI Agents

### Working In This Directory

- **Read `CLAUDE.md` first** — it captures the non-negotiable architectural constraints. Then the specific `.claude/skills/` for the task.
- **Skeleton stage**: expect to create types from scratch rather than modify existing code. Follow naming tables in CLAUDE.md / `kun-framework-conventions`.
- **Do not edit `Directory.Build.props` or `Directory.Packages.props` without reason** — target framework, nullable, analyzer settings, package versions are centralized there. Never duplicate them in individual csproj files.
- Commits follow Conventional Commits with `scope` = module name minus `Kun.` prefix (e.g. `feat(saga): ...`).

### Testing Requirements

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release --no-build
dotnet format --verify-no-changes
```

CI additionally runs AOT publish with `/warnaserror` over `Kun.Gateway`, `Kun.Mediator`, `Kun.Core`, `Kun.AspNetCore`, `Kun.Grpc`. Any new reflection or unannotated JSON serialization will fail that gate.

### Common Patterns

- Four-layer dependency flow: **Core → Abstraction → Infrastructure → Host**, never reversed.
- Business failures return `Result` / `Result<T>`; exceptions are reserved for systemic faults.
- Outbox messages identify event types by **stable string IDs** (`order.placed.v1`), never `AssemblyQualifiedName`.
- JSON serialization goes through `KunJsonContext` (source-generated `JsonSerializerContext`) — parameterless `JsonSerializer.Serialize(obj)` is forbidden in AOT-candidate modules.

## Dependencies

### External

- .NET 10 SDK (`10.0.100`), EF Core 10, Dapper AOT, YARP 2.x, Grpc.AspNetCore, RabbitMQ.Client 7.x, StackExchange.Redis, OpenTelemetry 1.x, .NET Aspire 9.x (samples), xunit.v3 + Testcontainers (tests).

<!-- MANUAL: -->
