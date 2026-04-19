# Kun.Sample.AppHost

.NET Aspire AppHost that orchestrates the Kun Framework sample services for local development.

## Prerequisites

- .NET 10 SDK (`10.0.100+`, pinned in `global.json`)
- Docker Desktop (containers for PostgreSQL, RabbitMQ, Redis)
- Aspire workload: `dotnet workload install aspire`

> **Note**: The Aspire workload must be installed before this project can be built or run.
> If the build fails with `NETSDK1138` or a missing SDK error, run:
> ```bash
> dotnet workload install aspire
> ```

## Running

From the repo root:

```bash
dotnet run --project samples/Kun.Sample.AppHost
```

Or target the project directly:

```bash
dotnet run --project samples/Kun.Sample.AppHost/Kun.Sample.AppHost.csproj
```

## Aspire Dashboard

Once running, the Aspire Dashboard is available at **http://localhost:15888** (default port).

The dashboard shows:

| Panel | What it displays |
|---|---|
| Resources | Live status of all orchestrated services (Catalog, Order, Gateway, postgres, rabbitmq, redis) |
| Logs | Structured log streams from each service |
| Traces | Distributed traces across service boundaries (OpenTelemetry) |
| Metrics | Resource-level metrics (CPU, memory, request rates) |

## Orchestrated services

| Resource name | Project | Role |
|---|---|---|
| `catalog` | `Kun.Sample.Catalog` | Write/read API for the Catalog bounded context |
| `order` | `Kun.Sample.Order` | Write/read API for the Order bounded context |
| `gateway` | `Kun.Gateway` | YARP reverse proxy + S2S JWT issuer |
| `postgres` | Docker (PostgreSQL) | Shared relational store; `kun` database created automatically |
| `rabbitmq` | Docker (RabbitMQ + management plugin) | Integration event bus |
| `redis` | Docker (Redis) | Idempotent consumer store + distributed cache |

## Infrastructure management UIs

- **pgAdmin** — auto-launched alongside postgres; URL shown in Aspire Dashboard
- **RabbitMQ Management** — `http://localhost:<port>` shown in Aspire Dashboard (port is dynamic)

## Notes

- This project is `IsAotCandidate=false` — AOT is not applicable to AppHost projects.
- Do not add business logic here. This file is pure orchestration.
- Connection strings and service discovery URLs are injected automatically by Aspire at startup.
