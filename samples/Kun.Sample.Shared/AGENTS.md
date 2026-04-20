<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-04-20 | Updated: 2026-04-20 -->

# Kun.Sample.Shared

Shared composition helpers for Kun Framework sample services. This project is **not** a framework module — it is only referenced by projects under `samples/`.

## What it provides

### `OpenTelemetryExtensions.AddKunObservability(serviceName)`

A single extension method that wires OpenTelemetry traces + metrics into a sample service's DI container.

**Traces** subscribe to all Kun Framework `ActivitySource` names:
- `Kun.Mediator` — command/query dispatch spans
- `Kun.EventBus.Outbox` — outbox polling + publish spans
- `Kun.Grpc` — gRPC server/client spans
- `Kun.Saga` — saga step execution spans

Plus standard ASP.NET Core, HttpClient, and gRPC client instrumentation.

**Metrics** include ASP.NET Core and HttpClient instrumentation.

Both traces and metrics export via OTLP (configure `OTEL_EXPORTER_OTLP_ENDPOINT` or `appsettings.json`).

## Packages

| Package | Version | Notes |
|---|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.10.0 | Hosting integration |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.10.0 | HTTP server spans + metrics |
| `OpenTelemetry.Instrumentation.Http` | 1.10.0 | HttpClient spans + metrics |
| `OpenTelemetry.Instrumentation.GrpcNetClient` | 1.10.0-beta.1 | gRPC client spans (beta) |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.10.0 | OTLP gRPC/HTTP export |

## Usage

```csharp
// In a sample service Program.cs — before AddKunAspNetCore()
builder.Services.AddKunObservability("catalog");
```

## Constraints

- `IsAotCandidate=false` — OTel packages are not AOT-compatible; this project must never be referenced by AOT-candidate framework modules.
- Do not add domain, infrastructure, or framework code here — this project is wiring only.
