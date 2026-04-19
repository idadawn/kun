# Kun.Sample.AppHost

.NET Aspire AppHost that starts the entire Kun sample stack — PostgreSQL, RabbitMQ, Redis,
Catalog API, Order API, and Gateway — with a single command.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| .NET 10 SDK (`10.0.100+`) | Pinned in `global.json` |
| Docker Desktop | Must be running; containers are started automatically |
| Aspire workload | Install once: `dotnet workload install aspire` |

If the build fails with `NETSDK1138` or a missing workload error:

```bash
dotnet workload install aspire
```

---

## How to launch

From the repo root:

```bash
dotnet run --project samples/Kun.Sample.AppHost
```

Aspire prints the Dashboard URL to the console:

```
Login to the dashboard at: http://localhost:15888
```

Wait for all resources in the Resources panel to show a green status before sending requests.
PostgreSQL runs `EnsureCreated` at service startup, which takes a few seconds.

---

## What appears in the Aspire Dashboard

| Resource | Type | Role |
|---|---|---|
| `catalog` | .NET project | Write/read API for the Catalog bounded context |
| `order` | .NET project | Write/read API for the Order bounded context (Saga demo) |
| `gateway` | .NET project | YARP reverse proxy and S2S JWT issuer |
| `postgres` | Docker container | PostgreSQL 16; `kun` database created automatically |
| `rabbitmq` | Docker container | RabbitMQ with management plugin enabled |
| `redis` | Docker container | Idempotent consumer store |

Infrastructure UIs launched alongside the containers:

- **pgAdmin** — link shown in the Aspire Dashboard Resources panel next to `postgres`
- **RabbitMQ Management** — link shown next to `rabbitmq`

Service endpoints (ports are dynamic — read them from the Dashboard Endpoints column):

- Catalog API: `http://localhost:<catalog-port>`
- Order API: `http://localhost:<order-port>`
- Gateway: `http://localhost:<gateway-port>`

---

## Where to find traces and logs

- **Traces** panel — distributed OpenTelemetry traces across all service boundaries. Filter by
  service name to isolate a specific service, or search by `TraceId` to follow a request from
  HTTP ingress through CQRS handler, EF Core, Outbox, and saga steps.

- **Logs** panel — structured log streams per resource, filterable by severity. The Order
  service logs each saga step execution and each Outbox publish attempt.

- **Metrics** panel — request rates, latency percentiles, and container resource usage.

---

## Notes

- This project is `IsAotCandidate=false`. AOT is not applicable to Aspire AppHost projects.
- Connection strings and service discovery URLs are injected automatically by Aspire at runtime.
  Do not hardcode ports or hostnames in service configuration.
- Do not add business logic to this file. It is pure orchestration.
