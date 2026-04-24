# Changelog

本文档记录 Kun Framework 的所有显著变更。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Security — Resolve NU1902 / GHSA-g94r-2vxg-569j on OpenTelemetry.Api 1.12.0 (2026-04-24)

- **KN-SELF-015** closes the NU1902 advisory
  ([GHSA-g94r-2vxg-569j](https://github.com/advisories/GHSA-g94r-2vxg-569j),
  moderate severity) against `OpenTelemetry.Api 1.12.0`, which
  was entering the transitive graph of `Kun.Sample.Catalog` +
  `Kun.Sample.Tests` via
  `OpenTelemetry.Instrumentation.EntityFrameworkCore 1.12.0-beta.1`.
- **Fix** (`ce910a4`): `Directory.Packages.props` OpenTelemetry
  group bumped — `OpenTelemetry.Api` explicitly pinned to
  **1.15.3** (patched), plus companion pins on `OpenTelemetry` /
  `OpenTelemetry.Extensions.Hosting` / `OpenTelemetry.Exporter.*`
  to 1.12.0 to keep the group coherent. CPM now force-resolves
  the transitive dependency up to the patched line.
- **Operator impact**: `dotnet build Kun.sln -c Release
  -p:TreatWarningsAsErrors=true` returns to 0 errors
  (NuGetAudit no longer promotes NU1902 to error). No source-
  or behaviour-level changes; purely a dependency graph
  tightening. Downstream consumers of Kun that pin
  `OpenTelemetry.Api` directly should align on 1.15.3 to
  avoid a version conflict at restore.

### Added — Non-generic `Result` variant of ValidationResultPipelineBehavior (2026-04-24)

- **KN-SELF-012** closes **ADR-014 §F1** — the deferred
  non-generic `Result` variant of the Result-returning validation
  pipeline behavior originally shipped by KN-SELF-007.
- **`Kun.Validation.Behaviors.ValidationResultPipelineBehavior<TRequest>`** —
  new sibling class in the same file as the generic
  `ValidationResultPipelineBehavior<TRequest, TValue>`, constrained
  as `IPipelineBehavior<TRequest, Result>` where
  `TRequest : IRequest<Result>`. Same validator loop, same
  `"; "`-joined `Error.Validation("request.invalid", …)` shape;
  returns `Result.Fail(...)` instead of `Result<TValue>.Fail(...)`.
- **`KunMediatorBuilder.AddValidationResultBehavior<TRequest>()`** —
  new single-type-param overload on
  `KunValidationServiceCollectionExtensions`, closing for
  `IRequest<Result>` specifically. Mirrors the existing
  two-type-param overload's registration pattern.
- **Consumer impact**: void-returning commands (e.g.
  `DeleteUserCommand : IRequest<Result>`) that want validation
  failures surfaced as `Result.Fail` no longer need to either
  throw `ValidationException` (via the legacy
  `ValidationPipelineBehavior`) or hand-roll a try/catch
  adapter. Register per-request exactly like the generic
  variant: `builder.AddValidator<DeleteUserCommand, DeleteUserValidator>()
  .AddValidationResultBehavior<DeleteUserCommand>();`.
- **Tests**: `tests/Kun.Validation.Tests/ValidationResultPipelineBehaviorTests.cs`
  picks up 3 new scenarios mirroring the generic-variant path —
  validator-fail → `Result.Fail(request.invalid)`, multi-validator
  aggregation, and handler-level `Result.Fail` preserved
  untouched. 20/20 Kun.Validation tests pass (17 existing + 3 new).
- **AOT**: behavior and DI overload are both AOT-safe — no
  reflection, closed generics compile-time-known,
  `Kun.Validation` ILC 0-warning.

### Changed — HasPublicApi sweep complete across all framework modules (2026-04-24)

- **KN-SELF-010** finished the pilot started by KN-SELF-008
  (Kun.Observability) and KN-SELF-009 (Kun.Sandbox). The remaining
  11 modules now track strict public API via
  `PublicApiAnalyzers`:
  - `Kun.Audit`, `Kun.Audit.EntityFrameworkCore`
  - `Kun.Caching`, `Kun.Configuration`
  - `Kun.Identity`, `Kun.Identity.AspNetCore`
  - `Kun.MultiTenancy`, `Kun.MultiTenancy.EntityFrameworkCore`
  - `Kun.RateLimit`, `Kun.Security`, `Kun.Storage`
- Each got its csproj flag flipped to `<HasPublicApi>true</HasPublicApi>`,
  a seeded empty `PublicAPI.Shipped.txt`, and an auto-populated
  `PublicAPI.Unshipped.txt` (via `dotnet format analyzers
  --diagnostics=RS0016` — the Roslyn code fix walks the assembly
  and writes canonical entries in one pass).
- **`Kun.Identity.AspNetCore/SignInHelper.cs`** picked up a
  file-level `#pragma warning disable RS0026` (same
  shape-distinguished dual overload pattern as
  `Kun.Sandbox/Abstractions/IProcesses.cs`).
- **Post-sweep invariant**: `grep -rE
  "<HasPublicApi>false</HasPublicApi>" src/` now returns zero.
  Any new framework module must ship with the flag on, matching
  the convention across the entire `src/Kun.*` tree.
- **Operator impact**: any future public-symbol change across
  any Kun module fails the analyzer gate until reviewed and
  added to the relevant `PublicAPI.Unshipped.txt`. Closes the
  drift surface that existed before KN-SELF-008.

### Changed — Kun.Sandbox now tracks public API strictly (2026-04-24)

- **`src/Kun.Sandbox/Kun.Sandbox.csproj`** flipped
  `<HasPublicApi>false</HasPublicApi>` → `true`.
- **Seeded `PublicAPI.Shipped.txt`** (empty) and
  **`PublicAPI.Unshipped.txt`** enumerating the ≈15 public types
  and their members — `ISandboxProvider` / `ISandboxHandle` /
  `IProcesses` / `IFiles` / `SandboxState`, the three
  implementations (`SupervisorSandboxProvider`,
  `E2BCloudSandboxProvider`, `InMemorySandboxProvider`), the
  E2B-shaped DTOs (`SandboxCreateOptions`,
  `NeuroagentSandboxExtension`, `SecretScopeConfig`,
  `BrowserSessionConfig`, `FileEntry`, `ProcessRunOptions`,
  `ProcessResult`), `KunSandboxOptions`, and the
  `AddKunSandbox` extension. 120 entries; auto-emitted by
  `dotnet format analyzers --diagnostics=RS0016` (the Roslyn
  code fix) rather than hand-written.
- **`IProcesses.RunAsync` pre-existing dual-overload**
  (`string[]` vs `string` first param) suppressed with a
  file-level `#pragma warning disable RS0026` and an explanatory
  comment — overload resolution is unambiguous on parameter
  shape, not optional-param count, so the analyzer's
  backcompat rule does not apply.
- **Operator impact**: any future public-symbol change in
  `Kun.Sandbox` now fails the `PublicApiAnalyzers` check until
  reviewed and added. No runtime behaviour changes.
- Second module in the KN-SELF-008 pilot sweep. Remaining
  `HasPublicApi=false` modules are the `*.EntityFrameworkCore`
  family + `Kun.AspNetCore`; next cycle evaluates per-module.

### Changed — Kun.Observability now tracks public API strictly (2026-04-24)

- **`src/Kun.Observability/Kun.Observability.csproj`** flipped
  `<HasPublicApi>false</HasPublicApi>` → `true`.
- **Seeded `PublicAPI.Shipped.txt`** (empty — convention for a new
  tracking file) and **`PublicAPI.Unshipped.txt`** enumerating the
  current public surface: `KunObservabilityOptions` + every
  property, `KunObservabilityServiceCollectionExtensions.AddKunObservability`,
  `Enrichment.KunBaggageKeys` + `TenantId` / `UserId` /
  `OutboxMessageId` constants + `All` array.
- **Operator impact**: any future public-symbol change in
  `Kun.Observability` now fails the `PublicApiAnalyzers` check
  until the diff is reviewed and added to the `.Unshipped.txt`,
  matching the discipline in place on `Kun.Core`, `Kun.Mediator`,
  `Kun.Validation`, and other user-facing modules. No runtime
  behaviour changes.
- **Scope note**: pilot for the remaining
  `HasPublicApi=false` modules (e.g. `Kun.Sandbox`). Those flips
  will land as separate KN-SELF-xxx tasks — one module at a time
  to keep each `PublicAPI.Unshipped.txt` review small.

### Added — ADR-014 Result-returning validation behavior (2026-04-24)

- **docs/architecture/ADR-014-Validation-Result-behavior.md** —
  accepted. Design for a sibling pipeline behavior to the existing
  exception-throwing `ValidationPipelineBehavior`: on validator
  failure, returns
  `Result<TValue>.Fail(Error.Validation("request.invalid", ...))`
  instead of throwing. Directly consumes ADR-011 `Result<T>`
  composition.
- **`Kun.Validation.Behaviors.ValidationResultPipelineBehavior<TRequest, TValue>`** —
  new sealed class implementing
  `IPipelineBehavior<TRequest, Result<TValue>>` with
  `where TRequest : IRequest<Result<TValue>>`. Aggregates
  validator errors into one joined message; empty-validators
  fast-path.
- **`AddValidationResultBehavior<TRequest, TValue>`** extension on
  `KunMediatorBuilder` — explicit per-request-type registration,
  matches the existing `AddValidator<>` / `AddValidationBehavior<>`
  pattern. The generic constraint on `TResponse` cannot be
  expressed at the open-generic DI layer (ADR-014 §DI
  registration).
- **5 new tests** in
  `tests/Kun.Validation.Tests/ValidationResultPipelineBehaviorTests.cs`
  covering: empty validators pass-through, validator pass, validator
  fail → Result.Fail with `request.invalid` code, multi-validator
  aggregation with `;` separator, handler-level `Result.Fail` is
  **not** intercepted (domain error preserved).
- **Coexistence rule** with the throwing `ValidationPipelineBehavior`:
  register one or the other per request type, not both — doc
  guardrail in `src/Kun.Validation/AGENTS.md`.
- **Scope notes**: non-generic `Result` variant deferred as ADR-014
  §F1. Structured per-field error payload deferred as §F2. Source-
  generator-based bulk registration deferred as §F3. No new
  `<PackageReference>`; `Kun.Validation` keeps
  `IsAotCandidate=true`.

### Added — ADR-013 §F1 log-record enricher (2026-04-24)

- **docs/architecture/ADR-013-Kun.Observability-enricher.md** —
  amended with a new §F1 resolution section. The prior §Follow-ups
  F1 entry is now annotated "Resolved 2026-04-24 (KN-SELF-006)"
  with a pointer to the new section.
- **`Kun.Observability.Enrichment.KunBaggageLogRecordProcessor`**
  (internal) — `BaseProcessor<LogRecord>` that runs at `OnEnd`,
  reads `Activity.Current?.Baggage`, and mirrors whitelisted keys
  (`tenant.id`, `user.id`, `outbox.message_id`) onto
  `LogRecord.Attributes`. Idempotent: never overwrites an
  attribute the log statement set explicitly. No-op when no
  `Activity.Current` exists.
- **`KunObservabilityOptions.EnrichLogsFromBaggage`** — new flag,
  default `false` (opt-in, mirrors `EnrichActivitiesFromBaggage`
  posture). Setting it to `true` registers the processor into the
  logging pipeline at `AddKunObservability` time.
- **4 new tests** in `tests/Kun.Observability.Tests/LogBaggageEnrichmentTests.cs`
  via `OpenTelemetry.Logs.InMemoryExporter`: baggage→attribute
  happy path, flag-off path, caller-set-attribute-preserved path,
  no-active-activity no-crash path.
- **AOT / package deltas**: zero new `<PackageReference>`;
  processor and tests use only already-referenced OpenTelemetry
  APIs. `Kun.Observability` keeps `IsAotCandidate=true`.
- **Operator impact**: flipping both
  `EnrichActivitiesFromBaggage = true` and
  `EnrichLogsFromBaggage = true` on `AddKunObservability`
  options now produces Aspire / Jaeger / Kibana queries where the
  same `tenant.id=…` filter works uniformly across the trace UI
  and the log view — no hand-joining.

### Added — ADR-013 Kun.Observability baggage enricher (2026-04-24)

- **docs/architecture/ADR-013-Kun.Observability-enricher.md** —
  accepted. Baggage-driven activity enrichment: consumer code
  pushes `tenant.id` / `user.id` / `outbox.message_id` into
  OpenTelemetry baggage at the request boundary; the framework
  mirrors those whitelisted keys onto Kun-sourced span tags so
  Aspire / Jaeger operators can filter by tenant / user without
  hand-correlating spans to logs.
- **`Kun.Observability.Enrichment.KunBaggageKeys`** — public
  static class exposing the canonical baggage key names and
  `All` array for iteration.
- **`Kun.Observability.Enrichment.KunBaggageActivityProcessor`**
  (internal) — `BaseProcessor<Activity>` that runs at `OnEnd`
  for activities whose `ActivitySource.Name` is in
  `KunActivitySources.All` and mirrors the whitelisted baggage
  subset to span tags.
- **`KunObservabilityOptions.EnrichActivitiesFromBaggage`** —
  new flag, default `false` (opt-in per ADR-013 §Default
  posture). Setting it to `true` registers the processor into
  the `TracerProviderBuilder` at `AddKunObservability` time.
- **7 new tests in `tests/Kun.Observability.Tests/BaggageEnrichmentTests.cs`**
  — processor-level unit tests (manual `TracerProviderBuilder` +
  `AlwaysOnSampler` + `OpenTelemetry.Exporter.InMemory`) plus
  DI-wired integration tests verifying the option flag both on
  and off.
- **AOT / package deltas**: zero new `<PackageReference>` on
  `Kun.Observability.csproj`; module stays `IsAotCandidate=true`.
  `HasPublicApi` stays `false` for this module — tracking a
  future flip as a separate framework-wide task so every
  `Kun.*` module adopts strict API tracking together.
- **Out of scope** (ADR-013 F1–F3): log-record enricher,
  metric-dimension enricher, Aspire sample-dashboard demo.

### Added — ADR-012 Kun.Testing module (2026-04-24)

- **docs/architecture/ADR-012-Kun.Testing-module.md** — accepted. New
  top-level `Kun.Testing` module at the Abstraction layer:
  AOT-candidate, packable, depends on `Kun.Core` only, takes no
  third-party runtime `PackageReference`. Framework-agnostic
  (`KunAssertionException` on mismatch rather than xUnit-coupled
  throws) so consumers on any test framework consume uniformly.
- **`src/Kun.Testing`** — new project wired into `Kun.sln` in the
  `src/` folder. `IsAotCandidate=true`, `IsPackable=true`,
  `HasPublicApi=true`.
- **`Kun.Testing.KunAssertionException`** — framework-agnostic
  assertion failure type.
- **`Kun.Testing.Results.ResultAssertions`** — fluent extensions on
  `Result` and `Result<T>`: `ShouldBeSuccess` (returns `T` on
  generic variant for chaining), `ShouldBeFailure`,
  `ShouldHaveErrorCode(string)`, `ShouldHaveErrorType(ErrorType)`.
- **`Kun.Testing.Domain.DomainEventAssertions`** — fluent extensions
  on `IAggregateRoot`: `ShouldRaise<TEvent>`,
  `ShouldRaiseExactly<TEvent>(int count)`, `ShouldNotRaise<TEvent>`.
  AOT-safe via compile-time-known `OfType<TEvent>()` filtering.
- **30 new tests in `tests/Kun.Testing.Tests`** — every helper on
  both pass-through and mismatch paths, exception-message shape
  anchored per ADR-012 §Error-message contract.
- **Smoke-adoption in `tests/Kun.Core.Tests`** — 4 existing
  assertions in `Results/ResultTests.cs` converted to Kun.Testing
  helpers. Validates cross-project consumption before other tests
  pull it in; 85 Kun.Core.Tests still pass.
- **Out of scope** (follow-ups F1–F3 in ADR-012): Outbox message
  assertions (would require a non-AOT companion module
  `Kun.Testing.EntityFrameworkCore`), Testcontainers fixtures,
  `WebApplicationFactory<T>` wrappers, repo-wide mass conversion
  sweep.

### Added — ADR-011 Result<T> composition extensions (2026-04-24)

- **docs/architecture/ADR-011-Result-async-extensions.md** — accepted.
  Specifies the canonical Map / Bind / Tap / Ensure / Match surface
  (sync + async) for `Kun.Core.Results` before more downstream consumers
  hand-roll their own. Normative short-circuit and cancellation
  semantics; alternatives (`Then`, LINQ `Select`/`SelectMany`,
  `Flatten`) explicitly deferred.
- **`Kun.Core.Results.ResultExtensions`** — synchronous extensions on
  `Result` and `Result<T>`: `Tap`, `Ensure`, `Match`, `Bind` (plus
  `Result<T>.Map` / `Result<T>.Bind` into `Result<TOut>` or `Result`).
- **`Kun.Core.Results.TaskResultExtensions`** — async extensions on
  `Task<Result>` / `Task<Result<T>>` receivers. Each op has two
  overloads: a sync-delegate variant and an async-delegate variant
  with required `CancellationToken`. Return type stays on the Task
  rail.
- **`Kun.Core.Results.ValueTaskResultExtensions`** — mirror of
  `TaskResultExtensions` for hot-path handlers that return
  `ValueTask<…>` (Mediator pipeline, Saga step executors). Same
  two-overload shape per op; async-delegate variant accepts
  `Func<…, ValueTask<…>>` and returns `ValueTask<…>`.
- **Deliberate deviation from ADR-011 sketch**: `CancellationToken`
  on async-delegate overloads is required, not `= default`. RS0026
  (PublicAPI analyzer "do not add multiple overloads with optional
  parameters") flags the alternative; we prefer passing the analyzer
  over masking it with a blanket suppression. Callers who want no
  token pass `default` or `CancellationToken.None` explicitly.
- **41 new tests in `tests/Kun.Core.Tests/Results/ResultExtensionsTests.cs`**
  — sync success / failure / short-circuit, async Task + ValueTask
  parity, CT forwarding, pre-cancelled CT surfaces
  `OperationCanceledException`, null-argument guards.
- **Scope note**: additive only. `Kun.Core` stays AOT-candidate with
  zero third-party deps; `PublicAPI.Unshipped.txt` listing every new
  symbol. Migration of `neuroagent-csharp` handlers off hand-rolled
  patterns is a separate follow-up task (ADR-011 §Follow-ups F1).

### Added — P2-A Part 2 benchmarks CI workflow (2026-04-24)

- **.github/workflows/benchmarks.yml** — weekly cron (Mon 06:00 UTC) + PR-label
  trigger (`benchmarks` label) + manual dispatch. Runs the
  `Kun.Benchmarks.Grpc` project with `--exporters json markdown` and uploads
  `BenchmarkDotNet.Artifacts/**` as a named artifact (30-day retention).
- Matrix includes `jit` only for now; the `aot` matrix entry is sketched in a
  comment and unlocks once a `PublishAot=true` variant of the benchmarks
  project lands (Part 2 follow-up).
- Out of scope (future Part 3): a runbook that downloads the artifact + diffs
  against a stored baseline to fail PRs on allocation regression.

### Added — P2-A Part 1 BenchmarkDotNet scaffold (2026-04-24)

- **benchmarks/Kun.Benchmarks.Grpc** — new standalone BenchmarkDotNet console project
  (intentionally NOT in Kun.sln so routine `dotnet build Kun.sln` ignores it).
  Builds clean at `dotnet build benchmarks/Kun.Benchmarks.Grpc -c Release`.
- `ResultToRpcInterceptorBenchmarks` — microbenchmarks the public `Result<T>` pattern
  (the internal `KunResultToRpcInterceptor` is not benchmarkable from a non-friend
  assembly; the `Result<T>` paths are the real hot path the translator catches).
  Compares `Result<T>.Ok` / `Fail` / `EnsureSuccess` / IsFailure+Error.Code against
  a plain-return baseline. `[MemoryDiagnoser]` attached so allocation deltas are
  visible.
- Directory.Packages.props centrally pins `BenchmarkDotNet` to `0.14.0` under a new
  "Benchmarks" label.
- **docs/benchmarks/grpc-aot.md** — methodology doc + run command + Part-2-plan
  (AOT matrix in CI, cold-start target <100ms).
- **Scope note**: Part 1 is scaffolding. Part 2 adds the AOT publish matrix on CI,
  JIT-vs-AOT comparison tables, and a `.github/workflows/benchmarks.yml` weekly +
  PR-label trigger. Kun.sln integration is intentionally deferred — benchmarks are
  run on-demand, not as part of the regular build matrix.

### Added — P2-C Part 3 Redis-backed rate-limit store + guide (2026-04-24)

- **Kun.Gateway.RateLimiting.IRedisRateLimitStore** + `RateLimitHitResult` record —
  low-level primitive for distributed rate limiting across multiple Gateway pods.
- **RedisFixedWindowRateLimitStore** — default implementation using atomic
  `INCR` + `EXPIRE`-on-first-hit semantics so counters self-expire when the window
  elapses (no cleanup job required). Key format `{KeyPrefix}:{partitionKey}`,
  default prefix `"kun:rl"`.
- **RedisRateLimitStoreOptions** — `KeyPrefix` knob, bound from
  `Kun:Gateway:RateLimit:Redis` configuration section.
- **AddKunRedisRateLimitStore** DI extension — `TryAddSingleton` so consumer
  pre-registrations win. Requires `IConnectionMultiplexer` to be registered
  separately by the consumer.
- **Kun.Gateway** csproj: new `StackExchange.Redis` `PackageReference` (version
  already centrally pinned to 2.8.16).
- **Scope note**: this is a primitive, not a full `PartitionedRateLimiter<HttpContext>`
  integration. Consumers today call `store.HitAsync(...)` from custom middleware
  and translate rejections to HTTP 429. Framework-level integration with
  `AddRateLimiter` named policies is tracked as Part 3b (requires a custom
  `RateLimiter` subclass with 5+ abstract members).
- Tests: 11 new facts in `tests/Kun.Gateway.Tests` (argument guards, first-hit
  TTL behavior, subsequent-hit TTL preservation, key-prefix application, DI
  registration). Gateway test total 48 → 59, all green.
- **docs**: new `docs/guides/gateway-rate-limit.md` covering the 3×3 resolver ×
  policy matrix + Redis store recipe + landing checklist.

### Added — P2-C Part 2 sliding-window + token-bucket rate-limit policies (2026-04-24)

- **Kun.Gateway.RateLimiting.KunSlidingWindowRateLimiterPolicy** (internal) + public
  `KunSlidingWindowOptions` + `AddKunSlidingWindowRateLimiter()` DI extension. Named
  policy `kun-sliding-window`. Defaults: 100 permits per 1-minute window split into
  6 segments, immediate rejection on exceed (QueueLimit=0). Smoother enforcement than
  fixed-window at the cost of proportionally higher memory per partition.
- **Kun.Gateway.RateLimiting.KunTokenBucketRateLimiterPolicy** (internal) + public
  `KunTokenBucketOptions` + `AddKunTokenBucketRateLimiter()` DI extension. Named
  policy `kun-token-bucket`. Defaults: 100-token burst budget refilled at 20 tokens
  every 10 seconds (≈120/min steady state) with `AutoReplenishment=true`. Absorbs
  bursty traffic; use for webhook fanout / batch import APIs.
- Both policies reuse the existing `IRateLimitPartitionResolver` abstraction so the
  three partition resolvers (Tenant / IP / UserPrincipal) compose freely with any
  of the three policies (fixed-window / sliding-window / token-bucket) via the
  matching `AddKunXxxRateLimiter` call.
- Tests: 9 new unit tests in `tests/Kun.Gateway.Tests`. P2-C remaining for Part 3:
  Redis-backed shared counter for distributed rate limiting + Testcontainers
  concurrent-precision test + dedicated guides.

### Added — P2-C Part 1 user-principal rate-limit partition resolver (2026-04-23)

- **Kun.Gateway.RateLimiting.Resolvers.UserPrincipalRateLimitPartitionResolver** —
  third partition resolver flavour, alongside the existing IP and tenant resolvers.
  Reads the authenticated user's `sub` claim and returns it as the rate-limit
  partition key; falls back to `"anonymous"` when the claim is absent. Pair with
  `AddKunRateLimiter` for free-tier APIs where rate limits are per-account rather
  than per-organization.
- Tests: 6 new unit tests in `tests/Kun.Gateway.Tests`. P2-C remaining items
  (sliding-window / token-bucket policies, Redis-backed shared counter,
  Testcontainers concurrent-precision test, dedicated guides) tracked for Part 2.

### Added — P2-D Part 3 DatabasePerTenant runtime (2026-04-23)

- **Kun.EntityFrameworkCore.MultiTenancy** (additive):
  - `IDbConnectionResolver` — resolves per-tenant connection strings at runtime.
  - `TenantsConnectionOptions` — static tenant → connection-string map, bound via
    `Configure<TenantsConnectionOptions>` from the `Kun:MultiTenancy:Tenants` config
    section. Suitable for small deployments; larger setups swap in a custom resolver.
  - `OptionsBasedTenantConnectionResolver` — default resolver reading from
    `IOptionsMonitor<TenantsConnectionOptions>`; supports on-the-fly reloads.
  - `DatabasePerTenantProvisioner<TContext>` — `ITenantProvisioner` that runs
    `Database.MigrateAsync()` against the tenant's database. `DeprovisionAsync` is a
    no-op by design (dropping a tenant database is an operations concern requiring
    admin credentials and coordinated cleanup). Both methods are `virtual` for
    subclassing when the app owns DB lifecycle end-to-end.
  - `AddKunMultiTenancy<TContext>` DatabasePerTenant branch now registers the default
    resolver + provisioner (TryAddSingleton, so consumer pre-registrations win).
- **docs**: ADR status upgraded to Part 3 Accepted; roadmap-p2 Part 3 row updated.

### Added — P2-D Part 2 SchemaPerTenant runtime (2026-04-23)

- **Kun.EntityFrameworkCore.MultiTenancy** (additive, existing Kun.EntityFrameworkCore is
  trim-safe / non-AOT; no new external dependencies):
  - `ITenantDbContextFactory<TContext>` — explicit factory for tenant-scoped DbContext
    construction, used by provisioning / admin flows that target a specific tenant
    regardless of the ambient `ITenantContext`.
  - `TenantAwareModelCacheKeyFactory` — `IModelCacheKeyFactory` implementation that
    partitions EF Core's compiled-model cache by `KunDbContext.TenantIdForModelCacheKey`
    in addition to context type + design-time flag.
  - `SchemaPerTenantProvisioner<TContext>` — `ITenantProvisioner` implementation that
    runs `CREATE SCHEMA IF NOT EXISTS "{tenantId}"` + `Database.MigrateAsync()` at
    provisioning time. Deprovisioning is gated behind
    `KunMultiTenancyOptions.AllowDestructiveDeprovision` (default false); unsafe tenant
    IDs (anything outside `[A-Za-z_][A-Za-z0-9_-]{0,62}`) are rejected with
    `ArgumentException` before reaching the database.
  - `KunMultiTenancyOptions` — `Strategy` + `AllowDestructiveDeprovision` knobs.
  - `AddKunMultiTenancy<TContext>(options => ...)` DI entry: Shared delegates to
    the existing `AddKunTenantRls()` + `NullTenantProvisioner`; SchemaPerTenant registers
    the factory + RLS interceptor (defence-in-depth) + SchemaPerTenantProvisioner;
    DatabasePerTenant throws `NotImplementedException` at resolution time pointing to
    the pending Part 3 track.
- **docs**: `docs/architecture/adr-p2d-multitenancy.md` status upgraded to Part 2 Accepted;
  `docs/roadmap-p2.md` P2-D table row updated.

### Added — P2-D Part 1 multi-tenancy foundation types (2026-04-23)

- **Kun.Core.MultiTenancy** (additive, AOT-safe, zero new dependencies):
  - `TenantIsolationStrategy` enum — `Shared` / `SchemaPerTenant` / `DatabasePerTenant`.
    Part 1 lands the type; runtime wiring for `SchemaPerTenant` / `DatabasePerTenant`
    is deferred to P2-D Part 2 / Part 3 (see ADR below).
  - `TenantInfo` sealed record — `(string TenantId, string? DisplayName)` with a null /
    empty / whitespace guard on `TenantId`. DTO crossing module boundaries.
  - `ITenantProvisioner` interface — `ProvisionAsync(TenantInfo)` / `DeprovisionAsync(string)`
    idempotent tenant lifecycle hook. Non-null implementations land with Part 2 / Part 3.
  - `NullTenantProvisioner` no-op default (Singleton `.Instance`) for Shared strategy
    consumers who don't need tenant lifecycle plumbing.
- **docs**:
  - `docs/architecture/adr-p2d-multitenancy.md` — ADR for the 3-strategy architecture,
    including the explicit YAGNI decision that Shared remains on the existing
    `AddKunTenantRls()` entry point (no wrapper API is added until a second strategy
    ships).
  - `docs/roadmap-p2.md` P2-D section updated with the Part 1 / Part 2 / Part 3 split.

### Added — Sprint 1 gap-closure (2026-04-22)

- **Kun.Storage.Abstractions / Kun.Storage.Local / Kun.Storage.S3** (P0-1): three
  new packages landing `IFileStorage` with the seven SAD §6.4 operations, a
  `FrozenSet` of the four canonical "active web" MIME types that trigger
  force-download on S3 presigned URLs, a dev-grade local filesystem
  implementation, and an AWSSDK-based S3 implementation. S3 is the repo's first
  non-AOT library module (A6) — explicitly excluded from the CI AOT-zero-warning
  matrix; documented in `src/Kun.Storage.S3/AGENTS.md`.
- **Kun.EntityFrameworkCore**:
  - `KunDbContext.DefaultSchema` virtual property + auto-registered
    `SchemaConsistencyInterceptor` (P0-3 M5). Overriding `DefaultSchema` without
    calling `.UseKunSchema(schema)` on the options builder now throws
    `InvalidOperationException` with a remediation message at the first
    `SaveChangesAsync`. Outbox rows, tenant/audit columns, and the migrations
    history table all co-locate under the service schema.
  - `UseKunSchema(schema)` extension method on `DbContextOptionsBuilder` that
    wires `MigrationsHistoryTableSchema` through `RelationalOptionsExtension`.
    Provider-agnostic — stays out of the Npgsql dependency.
  - `TenantRlsTransactionInterceptor` + `AddKunTenantRls()` (P1-4). Opt-in
    defence-in-depth layer that emits
    `SELECT set_config('app.current_tenant_id', @tenant, true)` at every
    transaction start so PostgreSQL RLS policies enforce isolation even when a
    caller bypasses the EF global query filter. Hooks
    `DbTransactionInterceptor.TransactionStartedAsync` — PgBouncer
    transaction-pool safe.
- **Kun.EventBus**: `IEventTypeRegistry.GetAll()` returns every registered
  `(eventId, clrType)` pair in stable alphabetical order, enabling the
  `tools/Kun.IntegrationEvents.SchemaGen` JSON-Schema export tool (P1-1).
- **Kun.Gateway**:
  - `ITracedCurrentUser` sub-interface + `trace_id` claim on the minted S2S
    JWT (P0-2). Default `EmitTraceIdClaim = true`; kill-switchable.
  - `KunInternalJwksEndpoint.MapKunInternalJwks()` — internal-network JWKS
    route gated on `IHostEnvironment.IsDevelopment()` OR
    `KunGatewayOptions.ExposeInternalJwks = true` (P1-3). Fail-closed:
    Production returns 404 unless opted in.
  - `IS2sPrincipalResolver` + `NoOpS2sPrincipalResolver` (P1-6). Optional
    enrichment hook the issuer runs before claim mapping. Default registered
    via DI. `S2sTokenIssuer` grows a 4-arg ctor taking the resolver; the
    3-arg overload is kept obsolete with `DiagnosticId = "KUN0002"` for one
    release, scheduled for removal in `0.2.0`.
- **Kun.EventBus.Outbox**: `IOutboxWriter` primitive (P0-4). Non-aggregate
  enqueuers (future audit interceptor, storage event adapters) can drop
  `OutboxMessage` rows into the current transaction without implementing the
  full `IIntegrationEventBus` surface. `IntegrationEventOutboxAccumulator`
  resolves to both `IIntegrationEventBus` and `IOutboxWriter` from a single
  scoped instance so aggregates and non-aggregate collaborators share one
  in-request buffer.
- **samples/Kun.Sample.Catalog** and **samples/Kun.Sample.Order**: override
  `DefaultSchema => "catalog"` / `"order"`; design-time factories chain
  `.UseKunSchema(...)`. Migrations regenerated; outbox, saga, business tables,
  and `__ef_migrations_history` all land under the service schema.
- **samples/Kun.Sample.Catalog**: `AddOpenApi()` + `MapOpenApi()` guarded on
  `IsDevelopment()` as the reference wiring for **P1-7** (OpenAPI pipeline
  template).
- **samples/Kun.Sample.Storage**: new Minimal API sample demonstrating
  force-download via `Kun.Storage.Local` — no AWS credentials required.
- **tools/Kun.IntegrationEvents.SchemaGen**: console tool (non-AOT) that emits
  `{eventId}.schema.json` per registered integration event using
  `JsonSchema.Net.Generation`. Consumers provide a public static zero-arg
  factory method that returns `IEventTypeRegistry` (P1-1).
- **docs**:
  - `docs/roadmap.md` — consolidated public roadmap (replaces the 495-line
    `TODO.md` at repo root, merged 2026-04-22).
  - `docs/schema-per-service.md` (P1-8) — end-to-end recipe for running many
    Kun services against one Postgres with service-scoped schemas.
  - `docs/openapi-template.md` (P1-7) — source-gen-friendly OpenAPI wiring
    guidance with environment gating + CI smoke-test recipe.
  - `src/Kun.EntityFrameworkCore/README.md` (P1-5) — PgBouncer transaction-pool
    compatibility notes (required connection-string parameters) +
    RLS-with-explicit-transactions contract.
- **tests**:
  - `tests/Kun.EntityFrameworkCore.Tests` — new xunit.v3 project with schema /
    interceptor / RLS unit coverage (11 cases).
  - `tests/Kun.IntegrationTests/Persistence/{RlsEnforcementTests,TenantRlsInterceptorPgBouncerTests,PgBouncerCompatTests,SchemaPerServiceTests}` +
    `tests/Kun.IntegrationTests/Fixtures/PgBouncerPostgresFixture` — acceptance
    paths for P0-3 / P1-4 / P1-5, Docker-gated with `Assert.Skip` when
    containers aren't reachable.
  - `tests/Kun.Gateway.Tests` grows five new cases for
    `KunInternalJwksEndpoint` (gating matrix + document shape) and five for
    `IS2sPrincipalResolver` / obsolete-ctor compat.
- **Kun.Auditing** (P1-2): new AOT-candidate module wiring an EF Core
  `SavingChanges` interceptor that iterates `ChangeTracker.Entries()`,
  keeps only entity types registered via explicit
  `services.AddAuditable<TEntity>()` (no runtime attribute scan — A3
  path c), and enqueues one `AuditEventIntegrationEvent` per mutation
  through `IOutboxWriter` (the P0-4 primitive). Payload is the M4
  13-field record with stable event id `kun.audit.event.v1`; JSON via a
  source-generated `AuditJsonContext`. `AuditContextMiddleware` captures
  IP / User-Agent / TraceId into a scoped `AuditContext` before the EF
  interceptor runs. DI helpers: `AddKunAuditing`, `AddAuditable<T>`,
  `AddAuditingInterceptor(DbContextOptionsBuilder, IServiceProvider)`;
  stable event id wired via `IEventTypeRegistrar.RegisterAuditing()`.
- **Kun.EventBus** (architect M2): `IEventTypeRegistrar.Register<TEvent>`
  gains an overload that accepts a source-generated `JsonTypeInfo<TEvent>`;
  `IEventTypeRegistry.GetJsonTypeInfo(Type)` exposes it. The outbox
  accumulator now prefers the typed serializer when registered and falls
  back to reflection-based `JsonSerializer.Serialize(obj)` only when no
  info is supplied — the fallback path carries `[RequiresUnreferencedCode]`
  and `[RequiresDynamicCode]` annotations so the AOT analyzer flags
  callers that haven't migrated.
- **Kun.Gateway** (review #7): `MapKunJwks` + `MapKunInternalJwks` accept
  an optional `Func<HttpContext, bool>? additionalGate` that runs after
  the environment / `ExposeInternalJwks` check. Typical use: RFC1918
  allow-list for production deployments without service-mesh isolation.
- **Kun.Gateway** (review #2): `S2sTokenIssuer.IssueForAsync(ClaimsPrincipal, CancellationToken)`
  lets callers await the configured `IS2sPrincipalResolver` without
  `.GetAwaiter().GetResult()` blocking. The sync `IssueFor` stays for
  back-compat and delegates to the async path internally.
- **Kun.Gateway** (review #1): both JWKS endpoints share a DI-registered
  `JwksResponseBuilder` singleton that caches the JWK response keyed on
  public-key-file mtime. Rebuilds only on key rotation; typical downstream
  JWKS fetch is a single `File.GetLastWriteTimeUtc` stat.
- **Kun.Storage.Local** (review #3): streaming `MaxBytes` enforcement.
  `UploadAsync` now rejects oversized uploads mid-copy via a chunked
  `ReadAsync` loop backed by `ArrayPool<byte>` — no more "park a 1 GB
  payload on disk before deleting it" failure mode.
- **Kun.Storage** (review #6): `ObjectMetadata.ContentType` is now
  `string?`. `S3FileStorage.ListAsync` reports `null` instead of a
  synthetic `"application/octet-stream"` when the S3 listing API omits
  the content type — callers needing it follow up with
  `GetMetadataAsync` per item.

### Migration guide — `alpha.1` → `alpha.2`

All Sprint 1 changes are **additive**. `PublicAPI.Shipped.txt` is untouched;
every new member lives in `PublicAPI.Unshipped.txt`. Consumers upgrading
from `0.1.0-alpha.1` do not need to change code to keep the old behaviour,
but the new capabilities below require explicit opt-in:

1. **Per-service schemas** (P0-3). To run multiple Kun services against one
   Postgres instance without name collisions:
   - Override `DefaultSchema` on your `KunDbContext` subclass: `protected override string? DefaultSchema => "catalog";`.
   - Chain `.UseKunSchema("catalog")` on the options builder alongside
     `.UseNpgsql(...)`.
   - Regenerate migrations so they emit `schema: "catalog"` — see
     [docs/schema-per-service.md](../docs/schema-per-service.md).
   - `SchemaConsistencyInterceptor` auto-registers via `KunDbContext.OnConfiguring`
     and will throw at first `SaveChangesAsync` if you miss either step.
2. **trace_id claim** (P0-2). `S2sTokenIssuer` now emits a `trace_id` claim
   when `Activity.Current` is live AND an HTTP context is in scope (M3
   guard). Downstream services see it on `JwtCurrentUser` via
   `ITracedCurrentUser.TraceId`. Default on; kill-switch via
   `KunGatewayOptions.EmitTraceIdClaim = false`.
3. **IFileStorage** (P0-1). Add `.AddKunLocalFileStorage(...)` (dev) or
   `.AddKunS3FileStorage(...)` (prod) to compose storage. `Kun.Storage.S3`
   is non-AOT — exclude from your AOT publish matrix if you have one.
4. **IOutboxWriter** (P0-4). Non-aggregate code can now enqueue outbox
   messages via `IOutboxWriter.Enqueue<TEvent>(evt)`. Existing
   `IIntegrationEventBus.Publish<T>` remains — both resolve to the same
   scoped accumulator instance. Prefer `IOutboxWriter` from interceptors
   and infrastructure code; `IIntegrationEventBus` for aggregate flows.
5. **Tenant RLS** (P1-4). Opt-in via `services.AddKunTenantRls()`. Wire the
   resulting interceptor into your DbContext options with
   `.AddInterceptors(sp.GetRequiredService<TenantRlsTransactionInterceptor>())`.
   Only needed when the database has `CREATE POLICY` rules using
   `app.current_tenant_id`.
6. **IS2sPrincipalResolver** (P1-6). `S2sTokenIssuer`'s 3-arg ctor is
   marked `[Obsolete(DiagnosticId = "KUN0002")]` and scheduled for removal
   in `0.2.0`. Switch manual construction sites to the 4-arg ctor, passing
   `NoOpS2sPrincipalResolver.Instance` for the identity transform. Custom
   enrichment: register your own `IS2sPrincipalResolver` implementation
   before `AddKunGateway`. DI-wired consumers do not need code changes.
7. **Internal JWKS endpoint** (P1-3). `app.MapKunInternalJwks()` at
   `/internal/.well-known/jwks.json`. Gated: Development auto-on, Production
   returns 404 unless `KunGatewayOptions.ExposeInternalJwks = true`. The
   existing public `/.well-known/jwks.json` route is unchanged.
8. **PgBouncer transaction mode** (P1-5). Append
   `No Reset On Close=true;Max Auto Prepare=0` to any Npgsql connection
   string routed through PgBouncer; see
   [src/Kun.EntityFrameworkCore/README.md](../src/Kun.EntityFrameworkCore/README.md).
9. **OpenAPI** (P1-7). `builder.Services.AddOpenApi();` +
   `if (app.Environment.IsDevelopment()) app.MapOpenApi();`. Requires the
   `Microsoft.AspNetCore.OpenApi` package.
10. **JSON Schema export** (P1-1). `IEventTypeRegistry.GetAll()` enumerates
    every `(eventId, clrType)` pair in stable order — used by
    `tools/Kun.IntegrationEvents.SchemaGen`. Provide a public static
    zero-arg factory method that returns your registry to plug into the
    tool; see `tools/Kun.IntegrationEvents.SchemaGen/README.md`.
11. **Auditing** (P1-2). Opt-in via `services.AddKunAuditing()` +
    `services.AddAuditable<YourEntity>()` for each entity you want audited.
    Attach the interceptor to your DbContext with
    `options.AddAuditingInterceptor(sp)` inside the factory lambda.
    Wire the stable event id via
    `services.AddKunEventBus(b => b.RegisterAuditing())`.
12. **AOT-safe outbox serialization** (architect M2). Upgrade existing
    `bus.Register<T>("t")` calls to
    `bus.Register<T>("t", MyJsonContext.Default.T)` to thread a
    source-generated `JsonTypeInfo<T>` through the outbox accumulator.
    Reflection fallback still works for non-AOT consumers but now warns
    via `[RequiresUnreferencedCode]` at call sites.
13. **JWKS `additionalGate`** (review #7, optional). Pass a predicate to
    `MapKunJwks` / `MapKunInternalJwks` to restrict by IP range or header
    when NetworkPolicy / mesh isn't available. Default behavior unchanged
    when the parameter is omitted.
14. **`S2sTokenIssuer.IssueForAsync`** (review #2). Prefer the async
    overload for hot-path token minting; the sync `IssueFor` delegates to
    it via `.GetAwaiter().GetResult()` and stays available for existing
    synchronous call sites.

### Fixed

- **Kun.Gateway**: `S2sTokenIssuer` now injects `IHostEnvironment` and throws `InvalidOperationException` when the signing key file is missing in non-Development environments, preventing silent ephemeral-key self-signing in production.
- **Kun.Gateway**: `AddKunGateway` without a `reverseProxyConfig` section now registers a startup-time `IHostedService` that fails fast with a user-friendly message if no `IProxyConfigProvider` is registered, instead of deferring the error to the first proxied request.
- **Kun.Gateway.Tests**: regression tests added for production key-file safety guard, startup config validation, and double-registration behavior.

- **Kun.Saga**: registered `ISystemClock` in `AddKunSaga` (required by
  `SagaOrchestrator`); `SagaStepExecutor` now persists state via
  `DbContext.SaveChangesAsync` after each entry; forward execution advances
  step index and marks completed; reverse compensation walks back to 0 and
  marks compensated; `ISagaOrchestrator` moved from Singleton to Scoped to
  match its Scoped dependency `ISagaInstanceStore`.

### Added — M1 交付（阶段一：奠基）

**Kun.Core** (v1.1)
- `Domain/DomainException`：带 `Code` 字段的领域异常基类
- `Domain/IDomainEventDispatcher`：同进程内领域事件分发接口
- `MultiTenancy/NullTenantContext`：非多租户默认实现
- `Security/AnonymousCurrentUser`：未认证默认实现
- `Time/ISystemClock` + `Time/SystemClock`：可注入时钟，默认由 `TimeProvider.System` 支持
- 为已有公开类型（AggregateRoot / Entity / ValueObject / Result / Error / Auditing 标记）补齐 XML 注释
- 单元测试 32/32（Domain + Results）

**Kun.Mediator** (v1.2)
- 核心接口：`ISender`、`IPublisher`、`INotification`、`INotificationHandler<T>`、`IPipelineBehavior<TRequest, TResponse>`、`RequestHandlerDelegate<TResponse>`
- `KunMediator`：基于 `FrozenDictionary<Type, RequestHandlerBase>` 的 O(1) 无反射调度
- `KunMediatorBuilder`：手工注册 API —— `AddRequestHandler<TReq, TResp, THandler>()`、`AddPipelineBehavior<TReq, TResp, TBehavior>()`、`AddNotificationHandler<TNotification, THandler>()`
- `DependencyInjection/AddKunMediator` DI 入口
- 内置 Behavior：`LoggingBehavior`、`PerformanceBehavior`（500ms 阈值）、`TracingBehavior`（`ActivitySource("Kun.Mediator")`）
- AOT 安全：所有泛型类型参数带 `[DynamicallyAccessedMembers(PublicConstructors)]`，构建时零 trim 警告
- 单元测试 8/8（Dispatch + Pipeline 洋葱顺序）

**Kun.Validation** (v1.3)
- `IValidator<T>` + `ValidationResult` + `ValidationError` + `ValidationException`
- `Behaviors/ValidationPipelineBehavior<TReq, TResp>`：聚合所有验证器，invalid 时抛 `ValidationException`
- `DependencyInjection/KunValidationServiceCollectionExtensions`：`AddValidator<T, TValidator>()` + `AddValidationBehavior<TReq, TResp>()`
- 单元测试 12/12

**Kun.Sample.Minimal**（新增）
- AOT 候选的控制台样例，演示 Command / Query / Notification 经 `KunMediator` 的完整调度链路
- 附带 `ActivityListener` 观察 `TracingBehavior` 输出
- `dotnet run` 本地验证通过

### Added — 基础设施
- 项目初始化：13 个核心模块骨架
- 白皮书、贡献指南、6 个 Claude 专属 Skills
- `Directory.Build.props` / `Directory.Packages.props` 集中化构建与依赖管理
- CI 工作流：build、test、AOT 发布验证、layering 检查、dotnet format
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` 接入 Kun.Core / Kun.Mediator / Kun.Infrastructure / Kun.EventBus / Kun.Validation
- `scripts/check-layering.sh`：强制 Kun.Core 无第三方 PackageReference
- `CLAUDE.md` 顶层 + 30 个 `AGENTS.md` 分层导航文档
- `Version`：`VersionPrefix=0.1.0`，本地 `VersionSuffix=alpha.1`
- `develop` 分支作为开发主干

### Changed
- `Kun.AspNetCore` / `Kun.Grpc` csproj：从 `Microsoft.NET.Sdk.Web` 修正为 `Microsoft.NET.Sdk`（它们是库，不是 Host）
- `PublicAPI.Unshipped.txt` → `PublicAPI.Shipped.txt`：Kun.Core (137)、Kun.Mediator (32)、Kun.Validation (25) 的 API 冻结入库

### Deferred（M1 已决定延后的项，详见 docs/roadmap.md）
- `Kun.Mediator.SourceGenerator` / `Kun.Validation.SourceGenerator`：当前用手工注册 API 替代，SG 作为 stretch goal（ralplan ADR fallback 路径）
- `Kun.Validation` 属性包（`[Required]`、`[MaxLength]` 等）：无 SG 时属性无运行时意义，与 SG 一同回归
- Result-aware 验证 Behavior（返回 `Result.Fail(Error.Validation(...))`）：可在核心抛异常 Behavior 之上封装，follow-up
- AOT 启动时间 benchmark：本地 Windows 缺 C++ linker，延至 CI 或用户本地 Linux 跑

### Added — M2 交付（阶段二：持久化双引擎）

**Kun.EventBus** (v2.1, AOT-candidate abstractions)
- `OutboxMessage`：富领域模型，含 `MarkPublished` / `MarkFailed` / `IncrementAttempt` 状态转换方法
- `OutboxStatus`：`Pending` / `Published` / `Failed` 枚举
- `IntegrationEventBuffer`：scoped 累加器，在 `SaveChangesAsync` 前收集待发事件
- `EventTypeRegistry`：`FrozenDictionary<string, Type>` 支持的 O(1) 稳定事件类型查找；`IEventTypeRegistry` / `IEventTypeRegistrar` 接口
- `IIntegrationEventHandler<TEvent>` + `MessageContext`：消费端处理器契约
- `IIntegrationEventPublisher`：每条消息的 broker 传输抽象
- `IProcessedMessageStore`：幂等消费存储抽象；`InMemoryProcessedMessageStore`（测试用）、`RedisProcessedMessageStore`（SETNX + EX，键格式 `idemp:{consumer}:{messageId}`）
- `IOutboxMessageStore`：持久层扫描抽象
- `OutboxPublisherBackgroundService`：BackgroundService，可配置批量大小、轮询间隔、最大重试次数、指数退避；每条消息启动 `ActivitySource("Kun.EventBus.Outbox")` span，从 `TraceParent`/`TraceState` 列恢复父 trace
- `OutboxPublisherOptions`：BatchSize=50 / PollInterval=2s / MaxAttempts=5 / RetryBaseDelay=1s
- DI 构建器：`KunEventBusBuilder`（`.UseInMemoryIdempotency()` / `.UseRedisIdempotency()`）、`KunEventBusServiceCollectionExtensions`、`KunOutboxPublisherExtensions`
- 单元测试 11/11

**Kun.EntityFrameworkCore** (v2.2, trim-safe, IsAotCandidate=false)
- `KunDbContext`：`IMultiTenant` 全局查询过滤器 + `ISoftDelete` 软删除过滤器
- `OutboxMessageConfiguration`：EF Core `IEntityTypeConfiguration<OutboxMessage>`，含索引
- `EfUnitOfWork`：单次 `SaveChangesAsync` 原子提交聚合变更 + Outbox 行 + 领域事件分发
- `IntegrationEventOutboxAccumulator`：W3C `TraceParent`/`TraceState` 捕获（via `Activity.Current`）
- `UnitOfWorkPipelineBehavior`：Mediator pipeline behavior，写命令专用
- `NoOpDomainEventDispatcher`：默认无操作实现；`MediatorDomainEventDispatcher` 由样例服务提供
- `IUnitOfWork` 接口
- DI：`KunEntityFrameworkCoreServiceCollectionExtensions`
- 单元测试集成（Testcontainers PostgreSQL，Docker 不可用时自动跳过）

**Kun.Dapper** (v2.3, AOT-candidate)
- `IReadDbConnectionFactory`：读端连接工厂抽象；调用方负责 dispose
- `PageRequest`：1-based 页码 + `Offset` 计算属性
- `PagedResult<T>`：`TotalPages` / `HasNextPage` / `HasPreviousPage` + `Empty(PageRequest)` 工厂方法
- `ReadDbOptions`：`ConnectionString` 配置
- DI：`KunDapperServiceCollectionExtensions.AddKunDapper`
- 单元测试 10/10

### Added — M3 交付（阶段三：消息总线）

**Kun.EventBus** (v3.x 追加)
- `IIntegrationEventPublisher`：每条消息的 broker 传输接口（从 M2 正式确定契约）
- `IProcessedMessageStore` 完整实现（InMemory + Redis），含幂等键格式文档

**Kun.EventBus.RabbitMQ** (AOT-candidate，RabbitMQ.Client 7.x)
- `RabbitMqOptions`：ConnectionString / ExchangeName / ConsumerName / PrefetchCount / MaxDeliveryAttempts / IdempotencyTtl
- `RabbitMqConnectionProvider`：单例懒初始化连接，`AutomaticRecoveryEnabled=true`，`IAsyncDisposable`
- `RabbitMqIntegrationEventPublisher`：`IIntegrationEventPublisher` 实现；topic exchange；headers 含 traceparent/tracestate/tenantId/messageId/occurredAt；持久化消息
- `RabbitMqIntegrationEventSubscriber`：BackgroundService；`AsyncEventingBasicConsumer`；per-type durable 队列 + binding；prefetch；W3C trace 恢复；x-death 计数 → DLX
- `IdempotentConsumerDecorator`：handler 成功后调用 `IProcessedMessageStore.TryMarkProcessedAsync`
- `DeadLetterHandler`：`{exchange}.dlx` fanout + `{consumer}.dead-letter` 队列
- `ISubscriptionDispatcher` + `TypedSubscriptionDispatcher<TEvent>`：AOT 安全有类型调度，无 `MakeGenericType`
- `KunRabbitMqBuilder`：`.Subscribe<TEvent, THandler>(eventTypeId, typeInfo)` 流式注册
- `KunRabbitMqServiceCollectionExtensions.AddRabbitMq`
- 单元测试新增 6 个，总计 79/79 通过
- M3 3.4（Testcontainers.RabbitMq 轮转测试）延后至 CI

### Added — M4 交付（阶段四：Saga 编排器）

**Kun.Saga** (AOT-candidate)
- 抽象：`ISagaContext` / `ISaga<TContext>` / `ISagaStep<TContext>` / `ISagaOrchestrator`
- 异常：`SagaStepFailedException` / `SagaNotFoundException`
- 持久化：`SagaInstance`（富状态转换：`StartCompensating` / `Complete` / `Fail`）、`SagaStepQueueEntry`、`SagaState`（Running/Compensating/Completed/Compensated/Failed）、`StepDirection`（Execute/Compensate）
- `ISagaInstanceStore`：持久层抽象；`EfSagaInstanceStore`：EF Core 实现，含 `(state, timeout_at)` 和 `(processed_at, enqueued_at)` 索引
- `SagaOrchestrator`：原子 start + 首步 enqueue（与触发命令同一 EF Core 事务）
- `SagaStepExecutor`：BackgroundService，正向执行 + 逆序补偿；`ActivitySource("Kun.Saga")`，span 名 `saga.step.execute` / `saga.step.compensate`，tags: `saga.step_index` / `saga.saga_id` / `saga.direction` / `saga.name`
- `SagaTimeoutDetector`：BackgroundService，每 5 秒扫描超时实例并触发补偿
- `SagaRegistry`：`FrozenDictionary` + 闭包泛型委托，AOT 安全，无 `MakeGenericType`
- DI：`KunSagaBuilder` + `AddKunSaga` 扩展
- 单元测试 18/18

### Added — M5 交付（阶段五：宿主层、RPC 与安全网关）

**Kun.Security** (M5.1, AOT-candidate)
- `S2sTokenOptions`：Issuer / Audience（默认 `kun-internal`）/ Lifetime（默认 60s）/ SigningAlgorithm（默认 RS256，HS256 明确禁止）/ JwksUri / ValidIssuers
- `IKunJwksProvider` + `FileKunJwksProvider`：文件监视 JWKS 提供者
- `KunServiceAuthenticationExtensions.AddKunServiceAuthentication`：JwtBearer 验证，`ValidAlgorithms=["RS256","ES256"]`，HS256 被拒绝
- `JwtTenantContext`：从 `ClaimsPrincipal` 提取 `tenant_id` 实现 `ITenantContext`
- `JwtCurrentUser`：从 `ClaimsPrincipal` 提取 `sub` / `name` / `roles` 实现 `ICurrentUser`
- `PropagateS2sTokenHandler`：`DelegatingHandler`，将入站 `Authorization` 头复制到出站请求
- DI：`AddKunSecurity`（注册 `ITenantContext` + `ICurrentUser`）/ `HttpClientBuilder.AddKunServiceAuth`
- 单元测试 7/7（valid、HS256 拒绝、wrong aud、expired、key rotation）

**Kun.AspNetCore** (M5.2, AOT-candidate)
- `KunEndpointRouteBuilderExtensions`：`MapCommand<TCommand, TResp>` + `MapQuery<TQuery, TResp>`（ISender glue）
- `ErrorToProblemDetailsExtensions`：`Error.ToProblemDetails()` 映射（Validation→400 / NotFound→404 / Conflict→409 / Unauthorized→401 / Forbidden→403）
- `KunExceptionHandler`：`DomainException` → 400 ProblemDetails
- `KunHealthChecksExtensions`：健康检查注册
- DI：`KunAspNetCoreServiceCollectionExtensions`
- 单元测试 11/11

**Kun.Grpc** (M5.3, AOT-candidate)
- `KunResultToRpcInterceptor`：`ResultFailureException` → `RpcException` 状态码映射；`Error.Code` 复制到 trailers
- `KunServerAuthInterceptor`：强制 `HttpContext.User` 已认证且含 `tenant_id` claim
- `KunTracingInterceptor`：`ActivitySource("Kun.Grpc")` span，`rpc.system=grpc` / `rpc.method` tags；拦截器顺序：Tracing → Auth → ResultMapping
- DI：`AddKunGrpc` 链式注册三个拦截器
- 单元测试 13/13

**Kun.Gateway** (M5.4, AOT-candidate，Web Host)
- `KunGatewayOptions`：ExternalIssuer / ExternalAudience / ExternalJwksUri / InternalIssuer / InternalAudience / InternalTokenLifetime(60s) / SigningKeyPath / PublicKeyPath
- `S2sTokenIssuer`：加载 PEM RSA 私钥，铸造 RS256 JWT，复制 `sub` / `tenant_id` / `roles` 声明
- `S2sAuthTransform`：YARP `RequestTransform`，将 Authorization 头替换为内部 token
- `JwksEndpoint.MapKunJwks`：`/.well-known/jwks.json`，`kid` = SHA-256(modulus) 前 16 字符，AOT 安全（`JwksJsonContext`）
- `IRateLimitPartitionResolver` + `TenantRateLimitPartitionResolver`（claim-based）+ `IpRateLimitPartitionResolver`（X-Forwarded-For aware）
- `KunRateLimiterExtensions`：固定窗口 100 req/min，可插拔分区解析器
- DI：`AddKunGateway` 配置 YARP + S2S issuer + transform
- `Program.cs` 组合根 + `appsettings.json` 骨架
- 单元测试 12/12
- M5 5.5 合约测试（Docker 依赖，延后）

全解决方案：0 错误，0 警告，140/140 测试通过 + 2 集成跳过

### Added — M6 交付（阶段六：示例应用）

**Kun.Sample.Catalog** (M6.1)
- 领域：`Product` AggregateRoot，`Create` / `ChangePrice` / `AdjustStock` 触发 `ProductCreatedDomainEvent` / `PriceChangedDomainEvent` / `StockChangedDomainEvent`
- 契约：`ProductPublishedIntegrationEvent`（稳定 ID `product.published.v1`）/ `InventoryChangedIntegrationEvent`（`inventory.changed.v1`）
- 应用层：`CreateProductCommand` + 验证器 + Handler；`AdjustStockCommand`；`GetProductByIdQuery`（Dapper 读端）；`ProductCreatedHandler`（领域事件 → `IIntegrationEventBus` 桥接）
- 基础设施：`CatalogDbContext`（继承 `KunDbContext`）/ `ProductConfiguration` / `NpgsqlReadDbConnectionFactory` / `MediatorDomainEventDispatcher`
- API：`Program.cs` 组合根；`EnsureCreated` 启动引导；`POST /products` / `GET /products/{id}` / `/health`

**Kun.Sample.Order** (M6.2, Saga 展示)
- 领域：`Order` AggregateRoot，`MarkReserved` / `Paid` / `Shipped` / `Completed` + `Cancel`；`OrderLine` owned value object；`OrderPlaced` / `OrderCancelled` 领域事件
- Saga：`OrderCreationSaga`（4 步，`DefaultStepTimeout=30s`）
  - `ReserveInventoryStep` → `ProcessPaymentStep` → `ShipOrderStep` → `NotifyCustomerStep`
  - 每步含 `ExecuteAsync` + 幂等 `CompensateAsync`
  - `ProcessPaymentStep` 在 `SimulateFailure=="payment"` 时抛异常（演示补偿）
  - `ShipOrderStep` 在 `SimulateFailure=="timeout"` 时 sleep 60s（演示超时）
- 契约：`OrderCompletedIntegrationEvent`（`order.completed.v1`）
- 应用层：`PlaceOrderCommand` / `GetOrderById` / `GetSagaStatus` 查询
- 基础设施：`OrderDbContext` 应用 `ApplyKunSagaConfiguration`；`EfOutboxMessageStore`
- API Endpoints：`POST /demo/happy-path` / `POST /demo/trigger-payment-failure` / `POST /demo/trigger-timeout` / `GET /demo/sagas/{id}`

**Kun.Sample.AppHost** (M6.3, .NET Aspire 9.0)
- 编排：PostgreSQL（+pgAdmin）/ RabbitMQ（+management plugin）/ Redis / Catalog / Order / Gateway
- `WaitFor` 依赖链：各服务等待 postgres + rabbitmq 就绪后启动
- Gateway 引用 catalog + order 用于 YARP 服务发现
- 单命令启动：`dotnet run --project samples/Kun.Sample.AppHost`

**Kun.Sample.Shared** (M6.4, IsAotCandidate=false)
- `OpenTelemetryExtensions.AddKunObservability(serviceName)`：
  - Tracing：ActivitySources `Kun.Mediator` / `Kun.EventBus.Outbox` / `Kun.Grpc` / `Kun.Saga` + AspNetCore + HttpClient + gRPC client
  - Metrics：AspNetCore + HttpClient
  - OTLP exporter（默认指向 Aspire Dashboard collector）
- 框架模块（`src/Kun.*`）保持无 OTel 包依赖；OTel 仅在样例组合根注入
- Gateway 未注入可观测性（AOT-candidate，OTel 包尚未 AOT 安全）
- `OpenTelemetry.Api` 升级至 1.11.x（修复 GHSA-8785-wc3w-h8q6）

### Changed（M2–M6 期间）
- `Directory.Packages.props`：新增 Dapper 2.1.35 / RabbitMQ.Client 7.x / OpenTelemetry 1.11.x / YARP 等包版本
- `Kun.AspNetCore` / `Kun.Grpc` csproj：`IsAotCandidate=true` 确认（库，非 Host）
- `Kun.EntityFrameworkCore` csproj：`IsAotCandidate=false`（trim-safe but not AOT）
- `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`：Kun.Dapper / Kun.EventBus / Kun.EntityFrameworkCore / Kun.Saga / Kun.Security / Kun.Gateway / Kun.Grpc 接入 RS0016 公开 API 追踪

### Deferred（M2–M6 已决定延后的项）
- Source Generators（Mediator / Validation / Saga）：按 ADR fallback 路径继续使用手工注册
- Testcontainers 集成测试（M2 2.4 / M3 3.4 / M5 5.5）：Docker 依赖，CI 环境运行，本地跳过
- `AddKunDapper` API rework：`ReadDbOptions.ConnectionString` 为 `required init`，消费方需在回调内设置（当前 API 略显冗余）
- `IReadDbConnectionFactory` 默认 Npgsql 实现：框架不内置，由消费方提供（参见样例 `NpgsqlReadDbConnectionFactory`）
- `MapQuery` nullable TResponse via JsonTypeInfo：类型系统限制，follow-up
- Gateway 可观测性：OTel 包达到 AOT parity 后追加
- NuGet 发布仪式（手动，TBD）

## [0.1.0-phase1] - 2026-04-23

Phase 1 首个可对 NeuroAgent 集成发版。10 个新模块落地（Storage / Identity /
MultiTenancy / Audit / RateLimit / Caching / Observability / Security / Configuration /
Sandbox），全仓 AOT 一致性通过审计，Directory.Build.props 统一 AOT 策略，CI 新增
`aot-audit` 矩阵 job 与 `sample-aot-integration` job。

[Unreleased]: https://github.com/idadawn/kun/compare/v0.1.0-phase1...HEAD
[0.1.0-phase1]: https://github.com/idadawn/kun/releases/tag/v0.1.0-phase1
