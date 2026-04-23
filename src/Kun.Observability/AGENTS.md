<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-22 -->

# Kun.Observability

## Purpose

Unified OpenTelemetry wiring for Kun Framework consumers. Single entry
point — `AddKunObservability` — that:

- Pre-registers the four Kun ActivitySources (`Kun.Mediator`,
  `Kun.EventBus.Outbox`, `Kun.Grpc`, `Kun.Saga`) so framework spans are
  captured without consumer boilerplate.
- Enables ASP.NET Core / HttpClient / EF Core / .NET Runtime
  instrumentation via opt-in/opt-out toggles.
- Exports traces, metrics, and logs over OTLP. The endpoint defaults
  to the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable — which
  .NET Aspire sets automatically — or can be overridden via
  `KunObservabilityOptions.OtlpEndpoint`.
- Is idempotent: calling `AddKunObservability` twice in the same
  service collection registers OTel once.

## Layer and AOT

- Layer: **Infrastructure** — carries OpenTelemetry package
  dependencies so consumer hosts don't have to juggle their own
  instrumentation list.
- `<IsAotCandidate>true</IsAotCandidate>` — relies on source-generated
  paths in the OTel 1.11.x SDK + Protobuf exporter. If the AOT
  analyzer ever warns after an OTel upgrade, **stop-gate the upgrade**
  and document the regression in an ADR; do not silently flip
  `IsAotCandidate=false` to hide the warning.

## Key Types

| Type | Purpose |
|------|---------|
| `KunObservabilityOptions` | All switches — service identity, OTLP endpoint, per-instrumentation toggles, and per-pillar toggles (tracing / metrics / logging). |
| `KunObservabilityServiceCollectionExtensions.AddKunObservability` | Registers the OTel pipeline. Returns `IServiceCollection` for chaining and is safe to call multiple times on the same collection (see idempotency marker). |

## Default Toggles

| Option | Default | Flip to disable |
|--------|---------|-----------------|
| `TracingEnabled` / `MetricsEnabled` / `LoggingEnabled` | `true` | Skip the whole pillar |
| `IncludeAspNetCore` | `true` | Drop ASP.NET Core server spans + HTTP metrics |
| `IncludeHttpClient` | `true` | Drop outbound HTTP spans + metrics |
| `IncludeEfCore` | `true` | Drop EF Core SQL traces |
| `IncludeRuntime` | `true` | Drop GC / threadpool metrics |

W3C Trace Context propagation is the OpenTelemetry 1.x default and is
left untouched; no custom propagator is registered.

## Wiring

```csharp
// Zero-config — reads OTEL_EXPORTER_OTLP_ENDPOINT from env.
services.AddKunObservability();

// Typical host wiring with explicit service identity + endpoint.
services.AddKunObservability(o =>
{
    o.ServiceName = "neuroagent-platform";
    o.ServiceVersion = "1.4.2";
    o.OtlpEndpoint = new Uri("https://collector.internal:4317");
});

// Background worker that doesn't need HTTP instrumentation.
services.AddKunObservability(o =>
{
    o.ServiceName = "worker-outbox";
    o.IncludeAspNetCore = false;
    o.IncludeHttpClient = false;
});
```

## For AI Agents

- Do not fork this wiring back into consumer `ServiceDefaults` or
  sample composition roots. `Kun.Observability` is the single place
  where Kun framework ActivitySources get added, so framework
  telemetry stays consistent across services.
- Adding a new Kun ActivitySource? Register it here (string list in
  `KunObservabilityServiceCollectionExtensions`). Document the span
  name in `docs/observability/tracing-tour.md` in the same PR.
- Adding a new option? Default it to the value that preserves
  backward compatibility — consumers must be able to upgrade
  without code changes.
- OTel package upgrades that introduce trim / AOT analyzer warnings
  are **stop-gate**. Open an ADR referencing the failing warning and
  either pin the prior version or flip `IsAotCandidate=false` with
  explicit approval.
