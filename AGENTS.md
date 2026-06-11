<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-18 | Updated: 2026-04-25 -->

# Kun Framework

## Purpose

A .NET 10 cloud-native microservice framework with an AOT-first lean: strict DDD, dual-engine data access (EF Core write side + Dapper AOT read side), reflection-free CQRS dispatcher, Outbox-based event-driven messaging, in-house Saga orchestrator, zero-trust S2S JWT between services, first-class multi-tenancy, object storage abstraction (`IFileStorage`), and tamper-evident operation audit (hash-chained `AuditEntry`).

**Status**: M1–M6 code complete, Phase 1 neuroagent-consumer gap fully closed, first stable CalVer release (`v2026.6.12`) published to NuGet.org. 32 `src/Kun.*` modules with real implementations, all in `Kun.sln` and CI-gated. See `CHANGELOG.md` for the detailed delivery log and `docs/roadmap.md` for outstanding work.

## Key Files

| File | Description |
|------|-------------|
| `Kun.sln` | Solution with 32 src projects + 29 test projects (+ samples & benchmarks) |
| `Directory.Build.props` | Global MSBuild config: `net10.0`, nullable on, `TreatWarningsAsErrors`, AOT analyzers gated by `<IsAotCandidate>` |
| `Directory.Packages.props` | Central Package Management — all NuGet versions pinned here; csproj files do not set `Version=` |
| `global.json` | Pins .NET SDK 10.0.100 |
| `.editorconfig` | Code style enforced by `dotnet format` in CI |
| `CLAUDE.md` | Authoritative instructions for Claude Code working in this repo |
| `CONTRIBUTING.md` | Human-facing contribution rules — same constraints as CLAUDE.md, less dense |
| `README.md` | Project overview, quick-start, module matrix |
| `CHANGELOG.md` | Keep-a-Changelog format; CalVer since 2026-06-11 (historical entries SemVer) |
| `docs/roadmap.md` | Public roadmap: outstanding work, Phase 2 candidates, deferred items |

## Subdirectories

| Directory | Purpose |
|-----------|---------|
| `src/` | 32 framework modules `Kun.*` (see `src/AGENTS.md`) |
| `tests/` | Unit, integration, and saga test projects (see `tests/AGENTS.md`) |
| `samples/` | `Kun.Sample.Catalog` / `.Order` / `.Minimal` / `.AppHost` / `.Shared` / `.Tests` (see `samples/AGENTS.md`) |
| `docs/` | Whitepaper, module map, architecture ADRs, security (S2S JWT, multi-tenancy RLS, audit log), observability, neuroagent Phase 1 gap audit (see `docs/AGENTS.md`) |
| `.claude/` | Kun-specific Claude Skills that auto-load in Claude Code (see `.claude/AGENTS.md`) |
| `.github/` | CI workflow + issue/PR templates |

## For AI Agents

### Working In This Directory

- **Read `CLAUDE.md` first** — it captures the non-negotiable architectural constraints. Then the specific `.claude/skills/` for the task.
- **Status is "M1–M6 code done, validation pending"**: new features land as incremental commits; refactors must preserve the four-layer dependency flow and the `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` tracked surface.
- **Do not edit `Directory.Build.props` or `Directory.Packages.props` without reason** — target framework, nullable, analyzer settings, package versions are centralized there. Never duplicate them in individual csproj files.
- **Every subtask commits independently**. Project convention: one commit per task, Conventional Commits with `scope` = module name minus `Kun.` prefix (e.g. `feat(audit): ...`, `fix(saga): ...`).
- **Branch rule (hard)**: never commit or push to `main` directly. Trunk-based: cut `<type>/<task>` from the latest `main`, PR into `main`, merge only on green CI, delete the branch after merge. See `CLAUDE.md` §Branch workflow.

### Testing Requirements

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release --no-build
dotnet format --verify-no-changes
```

Integration tests (`tests/Kun.IntegrationTests`) require Docker + Testcontainers and skip gracefully when Docker is unavailable — CI runs them; local iteration can exclude the project with `--filter "FullyQualifiedName!~IntegrationTests"`.

CI additionally runs AOT publish with `/warnaserror` over the AOT-candidate modules (`Kun.Core`, `Kun.Mediator`, `Kun.Validation`, `Kun.EventBus`, `Kun.EventBus.RabbitMQ`, `Kun.Dapper`, `Kun.Saga`, `Kun.Security`, `Kun.Grpc`, `Kun.Gateway`, `Kun.Audit`). `Kun.EntityFrameworkCore` and `Kun.Storage` are trim-safe-only. Any new reflection or unannotated JSON serialization will fail the AOT gate.

### Common Patterns

- **Four-layer dependency flow**: Core → Abstraction → Infrastructure → Host — never reversed; `scripts/check-layering.sh` enforces Kun.Core has zero third-party `PackageReference` entries.
- **Business failures return `Result` / `Result<T>`**; exceptions are reserved for systemic faults.
- **Outbox messages identify event types by stable string IDs** (`order.placed.v1`), never `AssemblyQualifiedName`. Prefer the attribute form: `[IntegrationEventId("order.placed.v1")]` + `Register<TEvent>()` (AOT-strict consumers keep `Register<TEvent>(string)`).
- **JSON serialization goes through `KunJsonContext`** (source-generated `JsonSerializerContext`) — parameterless `JsonSerializer.Serialize(obj)` is forbidden in AOT-candidate modules.
- **Multi-tenancy is two-layer**: EF `IMultiTenant` global query filter (first line) + optional `TenantRlsConnectionInterceptor` with PostgreSQL RLS policies (defence in depth).
- **Auditable commands opt in**: `services.AddKunMediator(b => b.AddAuditBehavior<TCommand, TResponse>())` — queries are NOT audited by default.

## Dependencies

### External

- .NET 10 SDK (`10.0.100`), EF Core 10, Dapper AOT, YARP 2.x, Grpc.AspNetCore, RabbitMQ.Client 7.x, StackExchange.Redis, OpenTelemetry 1.x, .NET Aspire 9.x (samples), AWSSDK.S3 (Kun.Storage), xunit.v3 + Testcontainers (tests, including Testcontainers.Minio).

<!-- MANUAL: -->
