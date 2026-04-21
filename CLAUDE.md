# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 🛑 Specs-first rule (READ BEFORE YOU WRITE ANY CODE)

**Never code before reading the relevant specs.** Kun is a framework that downstream services (`neuroagent-csharp`, others) consume as a dependency. Framework drift is far more expensive to fix than delayed delivery. If you're unsure what belongs in Kun vs in the consumer — stop and re-read.

**Mandatory read-first list** (in this order for any new task):

1. **This file** (`CLAUDE.md`) — the non-negotiable constraints below (4-layer deps, AOT rules, naming, CQRS, Outbox, Saga, multi-tenancy, `Result<T>` vs exceptions). CI rejects violations.
2. **`AGENTS.md`** (repo root) — agent-oriented guidance.
3. **`CONTRIBUTING.md`** — PR + commit + release process. Must follow.
4. **`README.md`** — framework overview and module map.
5. **`docs/whitepaper.md`** — design intent and boundaries (what Kun *is* and *is not*).
6. **`docs/getting-started.md`** — canonical module wiring patterns.
7. **`docs/architecture/`** — deep architectural specs; read the one(s) matching the module you're about to touch.
8. **`docs/security/`** — if anything security-adjacent (auth, tokens, PII, audit).
9. **`docs/observability/`** — if anything touches metrics / traces / logs.
10. **`docs/guides/`** — how-to guides; likely relevant to most tasks.
11. **`docs/neuroagent-phase1-gap.md`** — Phase 1 consumer (neuroagent) gap audit; shows what's urgent vs deferred.
12. **`TODO.md`** — current roadmap / what's intentionally not done yet.
13. **Module-level `src/Kun.<Module>/AGENTS.md`** — per-module conventions. Always read the AGENTS.md of every module you're about to modify **and** every module you're about to reference (one-hop).
14. **`Directory.Build.props`** + **`Directory.Packages.props`** + **`global.json`** — build conventions, central package management, SDK pin. Never add a `<PackageReference Version="...">` in a csproj; CPM handles versions.
15. **`samples/Kun.Sample.*`** — how consumers wire Kun. Use as reference for API ergonomics.

**What counts as "随意开发" (forbidden)**:

- Writing code in a module without reading its `AGENTS.md` first.
- Adding a NuGet package without updating `Directory.Packages.props` CPM entry.
- Introducing a dependency that violates the 4-layer flow (Host → Infrastructure → Abstraction → Core).
- Adding a `[JsonPropertyName]` / EF `[Column]` / validation attribute on a type in `Kun.Core`.
- Publishing API with non-AOT-compatible patterns in an `IsAotCandidate=true` module without updating the csproj flag and justifying in the PR.
- Inventing a new cross-cutting concern (auth / audit / multi-tenancy / eventbus) without an ADR in `docs/architecture/`.
- Committing without running `dotnet format --verify-no-changes` locally — CI enforces this.

**Task-dispatch contract** (for the main coordinator dispatching to `claude-kun`):

Every Kun task brief MUST include a numbered list of the specs to read first (from the list above, scoped to what's actually relevant). If a brief does not name the specs, reply "need specs list before coding" and pause.

## Repository status

Kun Framework is at the **project-skeleton stage**: solution, csproj files, `Directory.Build.props`, `Directory.Packages.props`, CI, docs, and `.claude/skills/` are in place. Most `src/Kun.*` projects contain no `.cs` files yet; the sample under `samples/Kun.Sample.Catalog` is a placeholder. When asked to "add X", expect to be creating types from scratch rather than modifying existing code — follow the conventions below strictly.

## Common commands

All commands run from the repo root unless stated. Requires .NET 10 SDK `10.0.100+` (pinned in `global.json`) and Docker (for integration tests via Testcontainers).

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release --no-build
dotnet format --verify-no-changes          # CI gate: code style
```

Run a single test project / test:

```bash
dotnet test tests/Kun.Mediator.Tests -c Release
dotnet test tests/Kun.Core.Tests --filter "FullyQualifiedName~SomeTestClass.SomeMethod"
```

AOT publish verification (CI enforces **zero warnings** — `/warnaserror`):

```bash
dotnet publish src/Kun.Gateway  -c Release -p:PublishAot=true /warnaserror
dotnet publish src/Kun.Mediator -c Release -p:PublishAot=true /warnaserror
# CI loops over: Kun.Gateway, Kun.Mediator, Kun.Core, Kun.AspNetCore, Kun.Grpc
```

Sample app (once implemented) via .NET Aspire:

```bash
dotnet run --project samples/Kun.Sample.Catalog/Kun.Sample.Catalog.AppHost
# Aspire Dashboard: http://localhost:15888
```

## Architectural constraints (non-negotiable — CI / review will reject violations)

### Four-layer dependency flow

Dependencies flow **downward only**:

```
Host        : Kun.AspNetCore · Kun.Grpc · Kun.Gateway
Infrastructure: Kun.EntityFrameworkCore · Kun.Dapper · Kun.EventBus.RabbitMQ · Kun.Saga
Abstraction : Kun.Mediator · Kun.Infrastructure · Kun.EventBus · Kun.Security · Kun.Validation
Core        : Kun.Core   (BCL only — no third-party packages, not even EF Core or Dapper)
```

`Kun.Core` is the hard constraint: it must not reference EF Core, Dapper, MediatR, ASP.NET, or any other external package. Domain base types (`AggregateRoot<TKey>`, value objects, domain events) live here and must carry **no ORM / JSON / validation attributes** — persistence mapping is pushed down to `IEntityTypeConfiguration<T>` in `Kun.EntityFrameworkCore`.

### AOT compatibility is per-module, opt-in via csproj

Each csproj sets `<IsAotCandidate>true|false</IsAotCandidate>`. `Directory.Build.props` then switches on `IsAotCompatible`, `IsTrimmable`, and the trim/AOT/single-file analyzers. `Kun.EntityFrameworkCore` is the one project deliberately marked `false` (trim-safe but not AOT); every other module is AOT-strict.

**Forbidden in AOT-candidate modules**:

- Runtime reflection: `Activator.CreateInstance`, `Type.GetMethod`, `MakeGenericType`, `Assembly.GetTypes`, `AppDomain.CurrentDomain.GetAssemblies`
- `System.Reflection.Emit`
- Parameterless `JsonSerializer.Serialize(obj)` — must pass `JsonTypeInfo` from `KunJsonContext` (a source-generated `JsonSerializerContext` where every serialized event type is registered)
- Dapper's classic `Query<T>` — use `[DapperAot]`-annotated methods so the source generator produces the reader code

### Dispatcher design: `Kun.Mediator`

CQRS dispatch is reflection-free. Handlers are discovered at compile time by a source generator and registered into a `FrozenDictionary<Type, RequestHandlerBase>` for O(1) lookup. This is why `Kun.Mediator` is AOT-candidate while MediatR is not — do not reintroduce reflection-based handler resolution.

### Write/Read split (CQRS, deliberate)

- **Write side** (Command handlers) → `Kun.EntityFrameworkCore`, EF Core repositories, Specification pattern, change tracking, Outbox enqueuing inside `SaveChangesAsync`.
- **Read side** (Query handlers) → `Kun.Dapper`, `[DapperAot]`-marked query methods, raw SQL, DTO projections. No change tracking, no aggregates loaded.

Never use EF Core in a Query handler or Dapper in a Command handler — it breaks the Pipeline behaviors, transactionality, and the AOT guarantees of the read side.

### Outbox (atomicity is the whole point)

In `Kun.EntityFrameworkCore`'s `UnitOfWork.SaveChangesAsync`:

1. Collect `IDomainEvent`s from `ChangeTracker.Entries<IAggregateRoot>()`, then `ClearDomainEvents()` on each.
2. Convert to `OutboxMessage` with a **stable event ID** (`{bounded-context}.{name}.v{version}`, e.g. `order.placed.v1`) looked up from `IEventTypeRegistry` — never `AssemblyQualifiedName`.
3. Serialize payload via `KunJsonContext.Default.GetTypeInfo(type)!` (AOT-safe).
4. Capture `Activity.Current?.Id` / `TraceStateString` as `TraceParent` / `TraceState` on the message so the background `OutboxPublisher` can resume the trace on publish.
5. `DbContext.SaveChangesAsync` commits aggregate changes + outbox rows in **one transaction**. This atomicity is the guarantee; do not split it.

Consumers are wrapped in `IdempotentConsumerDecorator<T>` backed by `IProcessedMessageStore` (Redis `SETNX + EX` by default). Delivery is at-least-once — consumer handlers must be safe to call repeatedly.

### Saga: Orchestration only

`Kun.Saga` is Orchestration (central coordinator), not Choreography. An `ISaga<TContext>` is an ordered list of `ISagaStep<TContext>` with `ExecuteAsync` and `CompensateAsync`. The orchestrator persists `SagaInstance` + enqueues the first step in the same EF transaction as the triggering command. Step execution and compensation run off a background executor driven by the Outbox. Compensation runs **in reverse order from the failed step** and must itself be idempotent (steps that never ran must still be safe to compensate).

### Zero-trust service-to-service auth

Two separate key pairs:

- **External JWT** — IdP-issued user token, validated only at `Kun.Gateway`.
- **Internal S2S JWT** — signed by `Kun.Gateway` with its own private key (RS256/ES256, never HS256), TTL 60s, `aud = "kun-internal"`, exposes a JWKS only on the internal network. Validated by every downstream service via `Kun.Security`.

Downstream services must extract identity (`sub`, `tenant_id`) from the S2S JWT claims. Trusting headers like `X-User-Id` is forbidden — anything not signed by the gateway private key is untrusted.

### Multi-tenancy

`ITenantContext` in `Kun.Core` is first-class. Any entity implementing `IMultiTenant` gets an automatic `TenantId` global query filter added by `KunDbContext.OnModelCreating`. Never bypass the filter with `IgnoreQueryFilters()` outside of explicit admin scenarios.

### Business failures use `Result` / `Result<T>`, not exceptions

`Kun.Core.Results`. Exceptions are reserved for systemic failures (concurrency, network, infra). gRPC endpoints can `result.EnsureSuccess()` and rely on the global `KunResultToRpcInterceptor` to translate `ResultFailureException` into the appropriate `RpcException` status code.

## Naming conventions

| Kind | Convention | Example |
|---|---|---|
| Assembly / root namespace | `Kun.{Module}` | `Kun.Mediator` |
| Command | verb + `Command` | `CreateOrderCommand` |
| Query | `Get`/`List`/`Search` + `Query` | `GetOrderByIdQuery` |
| Handler | `{Request}Handler` | `CreateOrderCommandHandler` |
| Domain event | past tense + `Event` | `OrderPlacedEvent` |
| Integration event | past tense + `IntegrationEvent` | `OrderPlacedIntegrationEvent` |
| Saga / step | `{Noun}Saga` / `{Verb}Step` | `OrderCreationSaga` / `ReserveInventoryStep` |
| Stable event ID (Outbox) | `{context}.{name}.v{n}` | `order.placed.v1` |

## csproj / dependency rules

- `TargetFramework`, `Nullable`, `LangVersion`, `TreatWarningsAsErrors`, analyzers — **set only in `Directory.Build.props`**, never in individual csproj files.
- Package versions live in `Directory.Packages.props` (central package management is on). `<PackageReference Include="X" />` — no `Version=` in csproj.
- Before adding a new NuGet package: check `Directory.Packages.props` first, unify versions if one exists, and verify AOT compatibility for AOT-candidate modules.
- Public API requires XML doc comments (`GenerateDocumentationFile=true`). CS1591 is explicitly excluded from `WarningsNotAsErrors` so missing docs warn but don't fail — production code should still document.

## Claude Skills in this repo

`.claude/skills/` contains six Kun-specific skills that auto-load. `kun-framework-conventions` is the prerequisite for all others — read it first on any coding task. For specific work also consult:

- `kun-ddd-modeling` — aggregates, value objects, domain events, specifications
- `kun-cqrs-mediator` — commands, queries, handlers, pipeline behaviors
- `kun-outbox-events` — integration events, stable IDs, idempotent consumers, event versioning
- `kun-saga` — saga definition, steps, compensation, timeouts
- `kun-testing` — unit / Testcontainers integration / saga / gRPC contract / AOT-publish tests

## Commit convention

Conventional Commits: `<type>(<scope>): <subject>`. Scope is the module name without the `Kun.` prefix (`mediator`, `saga`, `eventbus`, `security`, `gateway`, etc.). Example: `feat(saga): add timeout detection background service`.

## Release & versioning policy

Tag suffix is bound to the branch — the `Release` workflow (`.github/workflows/release.yml`) rejects mismatches:

- `main` → stable, **no suffix** (`v0.2.0`)
- `develop` → prerelease, **`-beta` suffix** (`v0.2.0-beta`)

Cut stable releases by merging `develop → main` first, then tagging from `main`. Do not re-tag a `-beta` commit as stable — tag the merge commit on `main`. See `CONTRIBUTING.md` §发布与版本规范 for details.
