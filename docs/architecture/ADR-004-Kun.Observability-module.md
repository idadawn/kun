# ADR-004: Introduce `Kun.Observability` Module

**Status**: Accepted
**Date**: 2026-04-22
**Author**: claude-kun (Kun framework specialist)
**Context**: `neuroagent-csharp` has been maintaining a `ServiceDefaults/Extensions.cs::ConfigureOpenTelemetry` helper that stitches together the OpenTelemetry SDK, four instrumentation packages, and an OTLP exporter. Every new consumer repository re-copies the same boilerplate. `samples/Kun.Sample.Shared/OpenTelemetryExtensions.cs` carries a partial twin that lacks EF Core and runtime instrumentation. `docs/observability/tracing-tour.md` already references an `AddKunObservability` extension as if it exists — this ADR makes that reference load-bearing.

---

## Decision

Introduce a new framework module `Kun.Observability` exposing a single
entry point:

```csharp
services.AddKunObservability(Action<KunObservabilityOptions>? configure = null);
```

The module owns:

- The canonical list of Kun ActivitySources
  (`Kun.Mediator`, `Kun.EventBus.Outbox`, `Kun.Grpc`, `Kun.Saga`) —
  kept in `KunActivitySources` so future sources are tracked in a
  single place.
- Per-instrumentation toggles (`IncludeAspNetCore`,
  `IncludeHttpClient`, `IncludeEfCore`, `IncludeRuntime`) that default
  to `true` so zero-config matches neuroagent's current shape.
- Per-pillar toggles (`TracingEnabled`, `MetricsEnabled`,
  `LoggingEnabled`) for background workers or specialized hosts that
  only need a subset.
- Service identity (`ServiceName`, `ServiceVersion`) projected into
  the OTel resource.
- Optional `OtlpEndpoint` override; when unset, the OTel SDK reads
  `OTEL_EXPORTER_OTLP_ENDPOINT` (which .NET Aspire sets
  automatically).
- An internal idempotency marker so repeated `AddKunObservability`
  calls in the same service collection register OTel once — important
  because shared libraries may call the extension defensively.

---

## Module Shape and AOT

| Property | Value |
|----------|-------|
| Layer | **Infrastructure** — carries OpenTelemetry package dependencies |
| `<IsAotCandidate>` | `true` — validated locally via `dotnet publish -p:PublishAot=true /warnaserror` up to the native-link stage (clang gap only) |
| Package feed | `OpenTelemetry` 1.11.2 + `OpenTelemetry.Instrumentation.*` 1.11.1 + `OpenTelemetry.Instrumentation.EntityFrameworkCore` 1.12.0-beta.1 + `OpenTelemetry.Instrumentation.Runtime` 1.11.1 |

The AspNetCore + HttpClient + EF Core + Runtime instrumentation
packages bundled into the module all pass the trim / AOT analyzer in
the 1.11.x line. `docs/observability/tracing-tour.md §Gateway
observability gap` describes a prior state where OTel instrumentation
was not AOT-safe; that note is now obsolete for AspNetCore / Http /
Runtime / EF Core (1.12.0-beta.1 onward) and Kun.Gateway can opt in
by calling `AddKunObservability`.

If an OTel package upgrade regresses AOT compatibility, treat it as
a stop-gate: pin the prior version or open a follow-up ADR for an
explicit `IsAotCandidate=false` flip. Do not silently downgrade the
module.

---

## Alternatives Considered

| Alternative | Why Rejected |
|-------------|--------------|
| A. Keep a per-consumer `ConfigureOpenTelemetry` helper | Drift between services. Four different service repos would maintain near-duplicate wiring. Already observed in the neuroagent-csharp vs. `Kun.Sample.Shared` divergence. |
| B. Put the wiring in `Kun.AspNetCore` | `Kun.AspNetCore` is host-layer and specific to HTTP. A background worker that wants OTel without ASP.NET Core would still pay the transitive dependency. Observability is a distinct concern. |
| C. Leave it in `Kun.Sample.Shared` | Samples are opinionated; copying an opinion into production services is a known anti-pattern. The framework needs a first-class module. |
| D. **New `Kun.Observability` module (selected)** | Clean separation from host layer; consumers opt in via a single `Add…` call; options carry the choice of pillars and instrumentation set. |

---

## Integration Points

Once `AddKunObservability` is wired in a host, the pipeline
automatically captures:

- `Mediator.Send {RequestTypeName}` — via `Kun.Mediator.TracingBehavior`
- `outbox.publish` — via `Kun.EventBus.OutboxPublisherBackgroundService`
- `grpc.server` — via `Kun.Grpc.KunTracingInterceptor`
- `saga.step.execute` / `saga.step.compensate` — via `Kun.Saga.SagaStepExecutor`

plus HTTP server, HTTP client, EF Core, and runtime metrics when the
respective toggles stay at their default of `true`.

W3C Trace Context propagation is the OpenTelemetry 1.x default;
`AddKunObservability` inherits that behaviour and does not register
any custom propagator.

---

## Consequences

### Positive

- neuroagent-csharp and any future consumer repo can replace the
  hand-rolled OTel helper with a single line.
- Framework upgrades that add a new ActivitySource only need a patch
  to `KunActivitySources.All` — consumers pick it up automatically.
- `Kun.Observability` is packaged as a NuGet; versioning is visible.

### Negative / Trade-offs

- One additional module to build, test, and publish per release.
- Transitive dependency surface grows — every host that calls
  `AddKunObservability` now pulls in the EF Core instrumentation
  package even if the host never touches EF Core. Consumers that care
  can flip `IncludeEfCore = false`; the instrumentation code still
  ships in the assembly but is not activated.

### Risks

| Risk | Mitigation |
|------|------------|
| OTel 1.12.x upgrade breaks AOT | Stop-gate rule; CI AOT matrix includes `Kun.Observability`. |
| Double registration in consumer composition | Idempotency marker short-circuits second-call wiring; covered by `AddKunObservability_IsIdempotent` test. |
| Consumer wants a non-OTLP exporter | Options can be extended — add a `ConfigureTracer` / `ConfigureMeter` hook as a follow-up without breaking the current surface. |

---

## Follow-ups

1. **Migrate `neuroagent-csharp/ServiceDefaults`** to call
   `builder.Services.AddKunObservability(o => o.ServiceName = …)`
   instead of the current local helper.
2. **Retire `samples/Kun.Sample.Shared/OpenTelemetryExtensions.cs`**
   in favor of the new module once the samples build green.
3. **Expose a `ConfigureTracer` / `ConfigureMeter` / `ConfigureLogger`
   extensibility hook** for consumers that want to add extra
   ActivitySources, metric instruments, or exporters.
4. **Revisit `Kun.Gateway` observability gap** noted in
   `tracing-tour.md §Gateway observability gap`. With OTel 1.11+
   AOT-clean, Gateway can opt in without losing AOT posture.

---

## References

- Handoff: task brief P1-KN-Observability
- `docs/observability/tracing-tour.md` — span catalog the module must
  keep current
- `neuroagent-csharp/src/NeuroAgent.ServiceDefaults/Extensions.cs` —
  the pattern being abstracted
- `samples/Kun.Sample.Shared/OpenTelemetryExtensions.cs` — to be
  retired
