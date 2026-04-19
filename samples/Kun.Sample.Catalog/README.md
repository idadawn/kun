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
