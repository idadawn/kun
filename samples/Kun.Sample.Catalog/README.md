# Kun.Sample.Catalog

A self-contained sample that demonstrates the Kun Framework CQRS and Outbox stack within a
single bounded context: the Catalog service manages `Product` aggregates.

---

## What it demonstrates

| Concept | Where |
|---|---|
| DDD aggregate root with domain events | `Domain/Product.cs` |
| Value object | `Domain/Category.cs` |
| Command + validator | `Application/Commands/CreateProductCommand.cs` and `CreateProductCommandValidator.cs` |
| Command handler (EF Core write side) | `Application/Commands/CreateProductCommandHandler.cs` |
| Query handler (Dapper AOT read side) | `Application/Queries/GetProductByIdQueryHandler.cs` |
| Domain event → integration event bridge | `Application/DomainEventHandlers/ProductCreatedHandler.cs` |
| Outbox pattern | `Infrastructure/CatalogDbContext.cs` + `EfUnitOfWork` from framework |
| Stable event IDs | `product.published.v1`, `inventory.changed.v1` registered in `Program.cs` |
| EF Core entity configuration | `Infrastructure/Configurations/ProductConfiguration.cs` |
| Dapper read connection factory | `Infrastructure/NpgsqlReadDbConnectionFactory.cs` |
| Composition root | `Program.cs` |

The full write-side pipeline is:

```
POST /products
  → ValidationPipelineBehavior    (Kun.Validation)
  → UnitOfWorkPipelineBehavior    (Kun.EntityFrameworkCore)
  → CreateProductCommandHandler
      → Product.Create()          [raises ProductCreatedDomainEvent]
      → db.Products.Add(product)
      → IUnitOfWork.SaveChangesAsync()
          → dispatches ProductCreatedDomainEvent in-process
              → ProductCreatedHandler enqueues ProductPublishedIntegrationEvent
          → serializes integration events to OutboxMessage rows
          → commits aggregate + Outbox in one EF Core transaction
```

---

## File map

```
Kun.Sample.Catalog/
├── Domain/
│   ├── Product.cs                          Aggregate root
│   ├── Category.cs                         Value object
│   └── Events/
│       ├── ProductCreatedDomainEvent.cs
│       └── StockAdjustedDomainEvent.cs
├── Application/
│   ├── Commands/
│   │   ├── CreateProductCommand.cs
│   │   ├── CreateProductCommandHandler.cs
│   │   ├── CreateProductCommandValidator.cs
│   │   ├── AdjustStockCommand.cs
│   │   └── AdjustStockCommandHandler.cs
│   ├── Queries/
│   │   ├── GetProductByIdQuery.cs
│   │   └── GetProductByIdQueryHandler.cs
│   └── DomainEventHandlers/
│       └── ProductCreatedHandler.cs
├── Contracts/
│   ├── ProductPublishedIntegrationEvent.cs
│   └── InventoryChangedIntegrationEvent.cs
├── Infrastructure/
│   ├── CatalogDbContext.cs
│   ├── NpgsqlReadDbConnectionFactory.cs
│   ├── MediatorDomainEventDispatcher.cs
│   └── Configurations/
│       └── ProductConfiguration.cs
├── CatalogJsonContext.cs                   Source-generated JsonSerializerContext
└── Program.cs                              Composition root
```

---

## How to run

### Via AppHost (recommended)

From the repo root, start the full stack with one command:

```bash
dotnet run --project samples/Kun.Sample.AppHost
```

Aspire provisions PostgreSQL automatically and injects the connection string. Open the Aspire
Dashboard (default `http://localhost:15888`) to find the Catalog service URL.

### Standalone with a local PostgreSQL

```bash
dotnet run --project samples/Kun.Sample.Catalog \
  --ConnectionStrings:CatalogDb "Host=localhost;Port=5432;Database=catalog;Username=postgres;Password=postgres"
```

The service calls `EnsureCreated` at startup, so no migration step is needed. This is suitable
for development only; a production deployment should use EF Core Migrations.

### Schema isolation

`CatalogDbContext` overrides `DefaultSchema => "catalog"` and the design-time factory
chains `.UseKunSchema("catalog")`, so every table — including `outbox_messages` and
`__ef_migrations_history` — lives under the `catalog` Postgres schema. The Order sample
uses the `order` schema; both can coexist in a single Postgres instance without name
collisions. See [docs/schema-per-service.md](../../docs/schema-per-service.md) for the
end-to-end recipe and the `SchemaConsistencyInterceptor` safety net.

### Audit & Sandbox wiring

`Program.cs` registers two additional Kun modules to demonstrate the minimal DI ergonomics
new in Phase 1:

```csharp
// Kun.Audit — fallback NullAuditWriter; call .UseEventBus() or register
// Kun.Audit.EntityFrameworkCore to enable persistence.
builder.Services.AddKunAudit();

// Kun.Sandbox — in-memory backend (no isolation, no egress) for dev-loop smoke tests.
// Production: UseSupervisor(baseUrl [, mTLS cert]) or UseE2BCloud(apiKey, teamId).
builder.Services.AddKunSandbox(opts => opts.UseInMemory());
```

These lines show the API shape; the sample does not exercise the writers/providers at
runtime. See `src/Kun.Audit/AGENTS.md` and `src/Kun.Sandbox/AGENTS.md` for
concrete backend choices.

---

## Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/products` | Create a product. Body: `CreateProductCommand` JSON. Returns the new product `Guid`. |
| `GET` | `/products/{id}` | Fetch a product by ID via the Dapper read side. Returns `ProductDto` or `404`. |
| `GET` | `/health` | Liveness check. Returns `{ "status": "healthy" }`. |

Example create request:

```bash
curl -s -X POST http://localhost:<port>/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Widget","categoryName":"Hardware","stockQuantity":100,"unitPrice":9.99}' \
  | jq .
```

Example get request:

```bash
curl -s http://localhost:<port>/products/<id> | jq .
```
